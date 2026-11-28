using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class PcCopyTests
{
    [AvaloniaFact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-11")]
    public async Task CopyChosenChatFromPhoneToPc_OpensReadOnlySurvivesRestartAndHoldsNoPlaintextOnDisk()
    {
        const string PhoneSecret = "E2E copied phrase turquoise-citadel-6193";
        const string BobSecret = "Bob reply sapphire-meadow-2847";

        var hub = new LoopbackDiscovery();
        await using var phone = new Device(hub, "Alice") { CanSendPcCopy = true };
        await using var bob = new Device(hub, "Bob");
        await using var pc = new Device(hub, "AlicePC") { CanReceivePcCopy = true };

        await Flows.SetUpPairAsync(phone, bob);
        await Flows.SendAsync(phone, PhoneSecret);
        await bob.WaitForTextAsync(PhoneSecret);
        await Flows.SendAsync(bob, BobSecret);
        await phone.WaitForTextAsync(BobSecret);
        await Flows.GoBackToContactsAsync(phone);

        await pc.LaunchAsync();
        await Flows.CreateIdentityAsync(pc);

        await Flows.ChooseFromMenuAsync(pc, "ReceiveCopyButton");
        await pc.WaitForTitleAsync(UiText.ReceiveCopyTitle);
        await pc.WaitForTextAsync(UiText.ReceiveCopyWaiting);
        Assert.Empty(pc.Window.GetVisualDescendants().OfType<TextBox>());
        var pairingCode = pc.ReadDisplayedQrCode();
        Assert.NotNull(pairingCode);

        await Flows.ChooseFromMenuAsync(phone, "SendCopyButton");
        await phone.WaitForTitleAsync(UiText.SendCopyTitle);
        Assert.Empty(phone.Window.GetVisualDescendants().OfType<TextBox>());
        phone.NextQrScan = pairingCode;
        phone.ClickButton("ScanButton");
        await phone.WaitUntilAsync(() => phone.CopyCandidateCheckBox("Bob") is not null, "Phone lists Bob among copy candidates");
        phone.Click(phone.CopyCandidateCheckBox("Bob")!);
        phone.ClickButton("CopySelectedButton");

        await phone.WaitForTextAsync(UiText.SendCopyCompletedOne);
        await pc.WaitForTextAsync(UiText.ReceiveCopyCompletedOne);

        pc.ClickButton("DoneButton");
        await pc.WaitForTitleAsync(UiText.ContactListTitle);
        await pc.WaitUntilAsync(() => pc.CopiedChatButton("Bob") is not null, "PC lists Bob under copied chats");
        Assert.Null(pc.ContactButton("Bob"));

        pc.Click(pc.CopiedChatButton("Bob")!);
        await pc.WaitForTitleAsync("Bob");
        Assert.True(pc.Shows(PhoneSecret));
        Assert.True(pc.Shows(BobSecret));
        Assert.Empty(pc.Window.GetVisualDescendants().OfType<TextBox>());

        await pc.QuitAsync();
        Assert.False(await pc.DatabaseFilesContainAsync("Bob"));
        Assert.False(await pc.DatabaseFilesContainAsync(PhoneSecret));
        Assert.False(await pc.DatabaseFilesContainAsync(BobSecret));

        await pc.LaunchAsync();
        await pc.WaitForTitleAsync(UiText.ContactListTitle);
        await pc.WaitUntilAsync(() => pc.CopiedChatButton("Bob") is not null, "PC keeps Bob's copied chat after restart");

        pc.Click(pc.CopiedChatButton("Bob")!);
        await pc.WaitForTitleAsync("Bob");
        pc.ClickButton("DeleteCopyButton");
        await pc.WaitUntilAsync(() => pc.TryNamed<Button>("DeleteCopyConfirmButton") is not null, "PC asks before deleting the copied chat");
        pc.ClickButton("DeleteCopyConfirmButton");
        await pc.WaitForTitleAsync(UiText.ContactListTitle);
        Assert.Null(pc.CopiedChatButton("Bob"));

        phone.ClickButton("DoneButton");
        await phone.WaitForTitleAsync(UiText.ContactListTitle);
        await Flows.OpenConversationAsync(phone, "Bob");
        Assert.True(phone.Shows(PhoneSecret));
        Assert.True(phone.Shows(BobSecret));
    }
}
