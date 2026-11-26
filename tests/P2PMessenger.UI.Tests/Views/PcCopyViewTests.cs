using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Discovery;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class PcCopyViewTests
{
    private static readonly CompositeFormat ContactTruncatedFormat = CompositeFormat.Parse(UiText.SendCopyCandidateContactTruncatedFormat);
    private static readonly CompositeFormat GroupTruncatedFormat = CompositeFormat.Parse(UiText.SendCopyCandidateGroupTruncatedFormat);
    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public async Task ContactList_ShowsCopyButtonOnlyOnMatchingPlatformRole()
    {
        await using var phone = await UiHarness.StartAsync("Phone", null, null, canSendPcCopy: true, canReceivePcCopy: false);
        var phoneList = Assert.IsType<ContactListViewModel>(phone.Shell.CurrentPage);
        var phoneView = new ContactListView { DataContext = phoneList };
        phone.Show(phoneView);
        Assert.True(UiHarness.Find<Button>(phoneView, "SendCopyButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(phoneView, "ReceiveCopyButton").IsVisible);

        await using var pc = await UiHarness.StartAsync("PC", null, null, canSendPcCopy: false, canReceivePcCopy: true);
        var pcList = Assert.IsType<ContactListViewModel>(pc.Shell.CurrentPage);
        var pcView = new ContactListView { DataContext = pcList };
        pc.Show(pcView);
        Assert.False(UiHarness.Find<Button>(pcView, "SendCopyButton").IsVisible);
        Assert.True(UiHarness.Find<Button>(pcView, "ReceiveCopyButton").IsVisible);

        phone.Messenger.Identity.Dispose();
        pc.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public async Task ReceiveCopyView_RendersReadablePairingQrCodeWithoutClipboardSecretAndClosesWhenHidden()
    {
        await using var pc = await UiHarness.StartAsync("PC", null, null, canSendPcCopy: false, canReceivePcCopy: true);
        var page = pc.Shell.Pages.ReceiveCopy();
        pc.Shell.Push(page);
        var view = new ReceiveCopyView { DataContext = page };
        var window = pc.Show(view);

        await UiHarness.WaitUntilAsync(() => page.HasQrCode);
        Assert.Equal(UiText.ReceiveCopyWaiting, UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.Empty(view.GetVisualDescendants().OfType<TextBox>());
        Assert.Null(pc.Platform.Clipboard);

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var pairingText = ReadQr(frame);
        Assert.NotNull(pairingText);
        Assert.StartsWith(CopyPairingCode.TextPrefix, pairingText, StringComparison.Ordinal);

        Assert.True(PcCopyService.TryParsePairingCode(pairingText, out var parsed, out _));
        pc.Shell.GoBack();
        await UiHarness.WaitUntilAsync(() =>
        {
            var check = pc.Messenger.PcCopy.ConnectAsync(parsed, CancellationToken.None).GetAwaiter().GetResult();
            if (check.Connected)
            {
                check.Sender.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return false;
            }

            return true;
        });

        page.OnShown();
        await UiHarness.WaitUntilAsync(() => page.HasQrCode);
        UiHarness.Flush();
        using var secondFrame = window.CaptureRenderedFrame();
        Assert.NotNull(secondFrame);
        var secondPairingText = ReadQr(secondFrame);
        Assert.NotNull(secondPairingText);
        Assert.NotEqual(pairingText, secondPairingText);
        page.OnHidden();
        pc.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public void CopyCandidateViewModel_WhenTruncated_DisclosesNewestCopiedCountOfTotal()
    {
        using var owner = Core.Identity.KeySet.Create();
        using var bob = Core.Identity.KeySet.Create();
        Assert.True(Core.Identity.DisplayName.TryCreate("Bob", out var bobName));
        Assert.True(Core.Identity.DisplayName.TryCreate("Study", out var groupName));

        var contactOrigin = CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey);
        var groupOrigin = CopyOriginId.ForGroup(owner.IdentityKey, Core.Groups.GroupId.Create());

        var truncatedContact = new CopyCandidateViewModel(new CopyCandidate(contactOrigin, bobName, IsGroup: false, MessageCount: 2, TotalMessageCount: 5), static () => { });
        Assert.True(truncatedContact.IsTruncated);
        Assert.Equal(2, truncatedContact.MessageCount);
        Assert.Equal(5, truncatedContact.TotalMessageCount);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ContactTruncatedFormat, 2, 5), truncatedContact.DetailText);

        var truncatedGroup = new CopyCandidateViewModel(new CopyCandidate(groupOrigin, groupName, IsGroup: true, MessageCount: 10, TotalMessageCount: 25), static () => { });
        Assert.True(truncatedGroup.IsTruncated);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, GroupTruncatedFormat, 10, 25), truncatedGroup.DetailText);
    }

    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public async Task ReceiveCopyView_WithNoLocalNetwork_ExplainsSameWifiRequirement()
    {
        await using var pc = await UiHarness.StartAsync("PC", NetworkSnapshot.None, null, canSendPcCopy: false, canReceivePcCopy: true);
        var page = pc.Shell.Pages.ReceiveCopy();
        pc.Shell.Push(page);
        var view = new ReceiveCopyView { DataContext = page };
        pc.Show(view);

        await UiHarness.WaitUntilAsync(() => page.CanRetry);

        Assert.False(page.HasQrCode);
        Assert.Equal(UiText.ReceiveCopyNoNetwork, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.True(UiHarness.Find<Button>(view, "RetryButton").IsVisible);
        pc.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-22")]
    public async Task SendAndReceiveViews_ScanAndCopyChosenChat_ShowsReadOnlyCopyWithSearchAndDelete()
    {
        await using var phone = await UiHarness.StartAsync("Alice", null, null, canSendPcCopy: true, canReceivePcCopy: false);
        await using var pc = await UiHarness.StartAsync("AlicePC", null, null, canSendPcCopy: false, canReceivePcCopy: true);
        var bob = await phone.ConnectPeerAsync("Bob");
        try
        {
            Assert.True((await phone.Messenger.SendAsync(bob.Identity.Keys.IdentityKey, "Hello from phone", CancellationToken.None)).IsQueued);
            Assert.True((await bob.SendAsync(phone.Messenger.Identity.Keys.IdentityKey, "Reply from Bob", CancellationToken.None)).IsQueued);
            await UiHarness.WaitUntilAsync(() => phone.Messenger.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count == 2);

            var pcList = Assert.IsType<ContactListViewModel>(pc.Shell.CurrentPage);
            var pcListView = new ContactListView { DataContext = pcList };
            pc.Show(pcListView);
            Assert.False(UiHarness.Find<TextBlock>(pcListView, "CopiedChatsHeading").IsVisible);

            pcList.ReceivePcCopyCommand.Execute(null);
            var receivePage = Assert.IsType<ReceiveCopyViewModel>(pc.Shell.CurrentPage);
            var receiveView = new ReceiveCopyView { DataContext = receivePage };
            var receiveWindow = pc.Show(receiveView);
            await UiHarness.WaitUntilAsync(() => receivePage.HasQrCode);
            UiHarness.Flush();
            using var qrFrame = receiveWindow.CaptureRenderedFrame();
            Assert.NotNull(qrFrame);
            var pairingText = ReadQr(qrFrame);
            Assert.NotNull(pairingText);

            var phoneList = Assert.IsType<ContactListViewModel>(phone.Shell.CurrentPage);
            phoneList.SendPcCopyCommand.Execute(null);
            var sendPage = Assert.IsType<SendCopyViewModel>(phone.Shell.CurrentPage);
            var sendView = new SendCopyView { DataContext = sendPage };
            phone.Show(sendView);
            Assert.Empty(sendView.GetVisualDescendants().OfType<TextBox>());

            phone.Platform.WillScan(pairingText);
            await sendPage.ScanCommand.ExecuteAsync(null);
            UiHarness.Flush();

            Assert.True(sendPage.IsConnected);
            var candidate = Assert.Single(sendPage.Candidates);
            Assert.Equal("Bob", candidate.Title);
            Assert.Equal(2, candidate.MessageCount);

            await sendPage.CopySelectedCommand.ExecuteAsync(null);
            UiHarness.Flush();
            Assert.Equal(UiText.SendCopyNothingSelected, sendPage.ErrorMessage);
            Assert.True(sendPage.IsConnected);

            candidate.IsSelected = true;
            await sendPage.CopySelectedCommand.ExecuteAsync(null);
            await UiHarness.WaitUntilAsync(() => sendPage.IsCompleted && receivePage.IsCompleted);
            Assert.Equal(UiText.SendCopyCompletedOne, UiHarness.Find<TextBlock>(sendView, "StatusText").Text);
            Assert.Equal(UiText.ReceiveCopyCompletedOne, UiHarness.Find<TextBlock>(receiveView, "StatusText").Text);

            receivePage.DoneCommand.Execute(null);
            UiHarness.Flush();
            Assert.Same(pcList, pc.Shell.CurrentPage);
            Assert.True(pcList.HasCopiedChats);
            Assert.True(UiHarness.Find<TextBlock>(pcListView, "CopiedChatsHeading").IsVisible);
            Assert.True(UiHarness.Find<DockPanel>(pcListView, "SearchPanel").IsVisible);

            pcList.SearchText = "bo";
            UiHarness.Flush();
            Assert.Single(pcList.CopiedChats);
            pcList.SearchText = "zzz";
            UiHarness.Flush();
            Assert.Empty(pcList.CopiedChats);
            Assert.True(pcList.HasNoMatches);
            pcList.ClearSearchCommand.Execute(null);
            UiHarness.Flush();

            var copiedItem = Assert.Single(pcList.CopiedChats);
            copiedItem.OpenCommand.Execute(null);
            UiHarness.Flush();
            var copiedPage = Assert.IsType<CopiedConversationViewModel>(pc.Shell.CurrentPage);
            var copiedView = new CopiedConversationView { DataContext = copiedPage };
            pc.Show(copiedView);

            Assert.Equal("Bob", copiedPage.Title);
            Assert.Contains("Read-only copy", UiHarness.Find<TextBlock>(copiedView, "ReadOnlyText").Text, StringComparison.Ordinal);
            Assert.Empty(copiedView.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(["Hello from phone", "Reply from Bob"], copiedPage.Messages.Select(static m => m.Text));
            Assert.Equal([UiText.CopiedConversationYou, "Bob"], copiedPage.Messages.Select(static m => m.Sender));

            copiedPage.AskDeleteCopyCommand.Execute(null);
            UiHarness.Flush();
            Assert.True(UiHarness.Find<Border>(copiedView, "DeleteCopyPanel").IsVisible);
            await copiedPage.DeleteCopyCommand.ExecuteAsync(null);
            UiHarness.Flush();

            Assert.Same(pcList, pc.Shell.CurrentPage);
            Assert.Empty(pcList.CopiedChats);
            Assert.Equal(2, phone.Messenger.Chats.MessagesWith(bob.Identity.Keys.IdentityKey).Count);
        }
        finally
        {
            await UiHarness.StopPeerAsync(bob);
            phone.Messenger.Identity.Dispose();
            pc.Messenger.Identity.Dispose();
        }
    }

    private static string? ReadQr(Bitmap frame)
    {
        var size = frame.PixelSize;
        var pixels = BitmapPixels.CopyBgra(frame, out var stride);
        var plane = Luminance.FromBgraOverWhite(pixels, size.Width, size.Height, stride);
        return new QrDecoder(false).DecodeLuminance(plane, size.Width, size.Width, size.Height);
    }
}
