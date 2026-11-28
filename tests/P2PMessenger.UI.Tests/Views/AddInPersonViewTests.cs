using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Controls;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class AddInPersonViewTests
{
    private const int StaticKeyOffset = 33;
    private const int StaticKeyLength = 32;

    [AvaloniaFact]
    [Trait("Story", "US-01")]
    [Trait("Story", "US-02")]
    public async Task AddInPerson_ShowsOwnQrCodeThatDecodesToTheCodeText()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);

        var shown = UiHarness.Find<QrCodeView>(view, "QrCode");
        Assert.Same(page.QrCode, shown.Matrix);
        Assert.Equal(harness.Messenger.Identity.CodeText, Decode(page.QrCode));
        Assert.Equal("Alice", UiHarness.Find<TextBlock>(view, "NameText").Text);
        Assert.Equal(harness.Messenger.Identity.Fingerprint.ToGroupedString(), UiHarness.Find<TextBlock>(view, "FingerprintText").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public async Task AddInPerson_ScannedNewCode_AddsTheContactInPersonWithTheKeyCheckedByYou()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        using var bob = KeySet.Create();
        var code = Code(bob, "Bob");
        harness.Platform.WillScan(code.ToText());

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var contact));
        Assert.Equal(CodeExchange.InPerson, contact.Exchange);
        Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
        Assert.Equal(ContactState.WaitingForFirstConnection, contact.State);
        Assert.Equal("Bob is added, with the key checked by you.", UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.Equal(code.Fingerprint.ToGroupedString(), UiHarness.Find<TextBlock>(view, "ScannedFingerprintText").Text);
        Assert.False(UiHarness.Find<Button>(view, "OpenChatButton").IsVisible);
        Assert.False(UiHarness.Find<TextBlock>(view, "ErrorText").IsVisible);
        Assert.False(page.HasPreview);
        var request = Assert.Single(harness.Platform.ScanRequests);
        Assert.Equal(UiText.ScanTitle, request.Title);
        Assert.Equal(UiText.ScanHint, request.Hint);
        Assert.Equal(UiText.ScanCancel, request.Cancel);
        Assert.Equal(UiText.ScanStarting, request.Starting);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-15")]
    public async Task AddInPerson_BothScan_TheAppsConnectAndTheChatOpens()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        var bob = await harness.StartPeerAsync("Bob");
        try
        {
            harness.Platform.WillScan(bob.Identity.CodeText);
            await page.ScanCommand.ExecuteAsync(null);
            UiHarness.Flush();
            Assert.False(page.IsConnected);

            var scanned = bob.Contacts.Check(harness.Messenger.Identity.CodeText);
            Assert.True((await bob.Contacts.AddAsync(scanned, CodeExchange.InPerson, CancellationToken.None)).IsSaved);
            await UiHarness.WaitUntilAsync(() => page.IsConnected);

            Assert.Equal("Bob scanned your code too, and your apps are connected.", UiHarness.Find<TextBlock>(view, "StatusText").Text);
            Assert.True(UiHarness.Find<Button>(view, "OpenChatButton").IsVisible);
            Assert.True(harness.Messenger.Contacts.TryGet(bob.Identity.Keys.IdentityKey, out var contact));
            Assert.True(contact.IsAdded);
            Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
            Assert.True(bob.Contacts.TryGet(harness.Messenger.Identity.Keys.IdentityKey, out var alice));
            Assert.Equal(KeyComparison.CheckedByYou, alice.KeyComparison);

            page.OpenChatCommand.Execute(null);
            var conversation = Assert.IsType<ConversationViewModel>(harness.Shell.CurrentPage);
            Assert.False(conversation.IsKeyNotCompared);
            Assert.False(conversation.IsWaiting);
        }
        finally
        {
            await UiHarness.StopPeerAsync(bob);
            harness.Messenger.Identity.Dispose();
        }
    }

    [AvaloniaTheory]
    [Trait("Story", "US-03")]
    [InlineData(QrScanStatus.NoCamera, "No camera was found on this device.")]
    [InlineData(QrScanStatus.CameraRefused, "The app may not use the camera.")]
    [InlineData(QrScanStatus.Failed, "The camera could not be started.")]
    public async Task AddInPerson_NoUsableCamera_SaysWhyAndOffersTheTextOrFileCode(QrScanStatus status, string expected)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        harness.Platform.WillScan(new QrScanResult(status, null));

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(expected, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.True(UiHarness.Find<Button>(view, "AddByCodeButton").IsVisible);
        Assert.Empty(harness.Messenger.Contacts.All);

        page.AddByCodeCommand.Execute(null);
        Assert.IsType<AddContactViewModel>(harness.Shell.CurrentPage);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task AddInPerson_ScanCancelled_ChangesNothing()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        harness.Platform.WillScan(QrScanResult.Cancelled);

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.False(UiHarness.Find<TextBlock>(view, "ErrorText").IsVisible);
        Assert.False(UiHarness.Find<Border>(view, "StatusPanel").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "AddByCodeButton").IsVisible);
        Assert.Empty(harness.Messenger.Contacts.All);
        Assert.True(page.ScanCommand.CanExecute(null));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaTheory]
    [Trait("Story", "US-02")]
    [InlineData("https://example.org/", "This QR code is not a P2PMessenger contact code.")]
    [InlineData("P2PM2-AAAA", "This code comes from another version of the app.")]
    [InlineData("P2PM1-AEAAAAAA", "This code is damaged.")]
    public async Task AddInPerson_ScannedSomethingElse_SaysWhyAndAddsNothing(string text, string expected)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        harness.Platform.WillScan(text);

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(expected, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.False(UiHarness.Find<Border>(view, "StatusPanel").IsVisible);
        Assert.Empty(harness.Messenger.Contacts.All);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task AddInPerson_OwnCodeScanned_IsRefused()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var (page, view) = Open(harness);
        harness.Platform.WillScan(harness.Messenger.Identity.CodeText);

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactOwnCode, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Empty(harness.Messenger.Contacts.All);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-04")]
    public async Task AddInPerson_ContactAddedByTextScannedInPerson_MarksTheKeyCheckedByYou()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        var added = await harness.AddContactAsync(bob, "Bob");
        Assert.Equal(KeyComparison.NotCompared, added.KeyComparison);
        var (page, view) = Open(harness);
        harness.Platform.WillScan(Code(bob, "Bob").ToText());

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var contact));
        Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
        Assert.Equal(CodeExchange.OtherChannel, contact.Exchange);
        Assert.Equal("Bob is already a contact. The key is now marked as checked by you.", UiHarness.Find<TextBlock>(view, "StatusText").Text);
        Assert.Single(harness.Messenger.Contacts.All);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-05")]
    public async Task AddInPerson_NewIdentityUnderASavedName_AsksFirstAndThenAddsItInPerson()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var oldBob = KeySet.Create();
        using var newBob = KeySet.Create();
        await harness.AddContactAsync(oldBob, "Bob");
        var (page, view) = Open(harness);
        harness.Platform.WillScan(Code(newBob, "Bob").ToText());

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(UiHarness.Find<Border>(view, "PreviewPanel").IsVisible);
        Assert.StartsWith("A saved contact is called Bob, but this code has a different key.", UiHarness.Find<TextBlock>(view, "PreviewHint").Text, StringComparison.Ordinal);
        Assert.Equal(UiText.AddContactAddAsNew, UiHarness.Find<Button>(view, "AddButton").Content);
        Assert.False(harness.Messenger.Contacts.TryGet(newBob.IdentityKey, out _));

        await page.AddCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(newBob.IdentityKey, out var contact));
        Assert.Equal(CodeExchange.InPerson, contact.Exchange);
        Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
        Assert.False(UiHarness.Find<Border>(view, "PreviewPanel").IsVisible);
        Assert.True(UiHarness.Find<Border>(view, "StatusPanel").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-05")]
    public async Task AddInPerson_NewConnectionKeys_AreAcceptedOnlyAfterAskingAndCountAsCheckedInPerson()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        using var newKeys = WithNewStaticKey(bob);
        var (page, view) = Open(harness);
        harness.Platform.WillScan(Code(newKeys, "Bob").ToText());

        await page.ScanCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.AddContactAcceptNewKeys, UiHarness.Find<Button>(view, "AddButton").Content);
        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var before));
        Assert.Equal(bob.StaticKey, before.Code.StaticKey);

        await page.AddCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(harness.Messenger.Contacts.TryGet(bob.IdentityKey, out var after));
        Assert.Equal(newKeys.StaticKey, after.Code.StaticKey);
        Assert.Equal(KeyComparison.CheckedByYou, after.KeyComparison);
        Assert.Equal(CodeExchange.InPerson, after.Exchange);
        Assert.StartsWith("The new connection keys of Bob are accepted", UiHarness.Find<TextBlock>(view, "StatusText").Text, StringComparison.Ordinal);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task ContactList_AddInPerson_OpensTheInPersonPage()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = list };
        harness.Show(view);

        Assert.Equal(UiText.ContactListAddInPerson, UiHarness.Find<Button>(view, "AddInPersonButton").Content);
        list.AddInPersonCommand.Execute(null);

        Assert.IsType<AddInPersonViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(UiText.InPersonTitle, harness.Shell.Title);
        harness.Messenger.Identity.Dispose();
    }

    internal static string? Decode(QrMatrix matrix)
    {
        var pixels = matrix.ToLuminance(3, out var width);
        return new QrDecoder(false).DecodeLuminance(pixels, width, width, width);
    }

    private static (AddInPersonViewModel Page, AddInPersonView View) Open(UiHarness harness)
    {
        var page = harness.Shell.Pages.AddInPerson();
        harness.Shell.Push(page);
        var view = new AddInPersonView { DataContext = page };
        harness.Show(view);
        return (page, view);
    }

    private static ContactCode Code(KeySet keys, string name)
    {
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        return ContactCode.Create(keys, displayName);
    }

    private static KeySet WithNewStaticKey(KeySet keys)
    {
        var exported = new byte[KeySet.ExportLength];
        try
        {
            Assert.True(keys.TryExport(exported, out _));
            RandomNumberGenerator.Fill(exported.AsSpan(StaticKeyOffset, StaticKeyLength));
            Assert.True(KeySet.TryImport(exported, out var changed));
            return changed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }
    }
}
