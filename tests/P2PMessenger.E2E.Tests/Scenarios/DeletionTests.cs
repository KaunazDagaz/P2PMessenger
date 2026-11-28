using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class DeletionTests
{
    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteChat_AfterConfirming_RemovesTheMessagesOnThisDeviceOnlyAndTheChatKeepsWorking()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob);
        await Flows.SendAsync(alice, "A message that will be deleted.");
        await bob.WaitForTextAsync("A message that will be deleted.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);
        await Flows.SendAsync(bob, "A reply that will be deleted too.");
        await alice.WaitForTextAsync("A reply that will be deleted too.");

        alice.ClickButton("DeleteChatButton");
        await alice.WaitUntilAsync(() => alice.TryNamed<Border>("DeleteChatPanel") is not null, "Alice is asked to confirm");
        Assert.True(alice.Shows("A message that will be deleted."));
        alice.ClickButton("DeleteChatConfirmButton");

        await alice.WaitForTextAsync(Flows.ChatDeletedNotice("Bob"));
        await alice.WaitForTextAsync(UiText.ConversationEmpty);
        Assert.False(alice.Shows("A message that will be deleted."));
        Assert.False(alice.Shows("A reply that will be deleted too."));
        Assert.True(bob.Shows("A message that will be deleted."));
        Assert.True(bob.Shows("A reply that will be deleted too."));

        await Flows.GoBackToContactsAsync(alice);
        Assert.Equal("Bob", Assert.Single(alice.ContactNames()));
        await Flows.OpenConversationAsync(alice, "Bob");
        await Flows.SendAsync(alice, "After the deletion.");
        await bob.WaitForTextAsync("After the deletion.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);
        Assert.False(alice.Shows("A message that will be deleted."));
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteAllData_AfterConfirming_ErasesTheDatabaseAndReturnsToIdentityCreation()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob);
        await Flows.SendAsync(alice, "Gone together with everything else.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);
        Assert.True(File.Exists(alice.DatabasePath));

        await Flows.GoBackToContactsAsync(alice);
        await Flows.ChooseFromMenuAsync(alice, "DeleteAllDataButton");
        await alice.WaitUntilAsync(() => alice.TryNamed<Button>("DeleteAllConfirmButton") is not null, "Alice is asked before everything is deleted");
        alice.ClickButton("DeleteAllConfirmButton");

        await alice.WaitForTitleAsync(UiText.CreateIdentityTitle);
        await alice.WaitForTextAsync(UiText.DeleteAllDataDone);
        Assert.False(File.Exists(alice.DatabasePath));
        Assert.False(alice.Shows("Gone together with everything else."));
        await bob.WaitForTextAsync(UiText.ContactNotReachable);
        Assert.True(bob.Shows("Gone together with everything else."));

        await Flows.CreateIdentityAsync(alice);
        Assert.Empty(alice.ContactNames());
    }
}
