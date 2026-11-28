using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class ChatTests
{
    [AvaloniaFact]
    [Trait("Story", "US-01")]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-11")]
    [Trait("Story", "US-16")]
    public async Task TwoPeople_AddEachOtherByCodeAndChat_BothSeeTheMessagesAndTheSenderSeesThemReceived()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await alice.LaunchAsync();
        await bob.LaunchAsync();

        await Flows.CreateIdentityAsync(alice);
        await Flows.CreateIdentityAsync(bob);
        await Flows.AddEachOtherAsync(alice, bob);
        await Flows.WaitUntilConnectedAsync(alice, bob);

        await alice.WaitForTextAsync(Flows.AddedNotice("Bob"));
        await bob.WaitForTextAsync(Flows.AddedNotice("Alice"));
        Assert.DoesNotContain(alice.Texts(), static text => text.Contains("verified", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(alice.TryNamed<Border>("KeyNotComparedPanel"));
        Assert.NotNull(bob.TryNamed<Border>("KeyNotComparedPanel"));

        await Flows.SendAsync(alice, "Hello Bob, this is Alice.");
        await bob.WaitForTextAsync("Hello Bob, this is Alice.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);

        await Flows.SendAsync(bob, "Hi Alice, it arrived.");
        await alice.WaitForTextAsync("Hi Alice, it arrived.");
        await bob.WaitForTextAsync(UiText.DeliveryReceived);
    }

    [AvaloniaFact]
    [Trait("Story", "US-11")]
    public async Task WideWindow_ShowsTheContactListBesideTheChatAndTheListStaysWhenTheChatCloses()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob);

        alice.Window.Width = 1200;
        await alice.WaitUntilAsync(
            () => alice.TryNamed<TextBox>("DraftBox") is not null && alice.ContactButton("Bob") is not null,
            "Alice sees the chat with Bob and the contact list together");
        await Flows.SendAsync(alice, "Sent with the list on the left.");
        await bob.WaitForTextAsync("Sent with the list on the left.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);

        alice.ClickButton("BackButton");
        await alice.WaitUntilAsync(
            () => alice.TryNamed<TextBox>("DraftBox") is null && alice.ContactButton("Bob") is not null,
            "the chat closes and the contact list stays");
        await alice.ClickUntilAsync(
            () => alice.ContactButton("Bob"),
            () => alice.TryNamed<TextBox>("DraftBox") is not null && alice.ContactButton("Bob") is not null,
            "Alice opens the chat with Bob from the list that stays beside it");
        Assert.True(alice.Shows("Sent with the list on the left."));
    }

    [AvaloniaFact]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-03")]
    [Trait("NFR", "NFR-06")]
    public async Task Message_ContactAppIsClosed_WaitsAndArrivesWhenTheContactStartsTheAppAgain()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob);

        await bob.QuitAsync();
        await alice.WaitForTextAsync(UiText.ContactNotReachable);
        await Flows.SendAsync(alice, "Sent while Bob's app was closed.");
        await alice.WaitForTextAsync(UiText.DeliveryWaiting);
        Assert.DoesNotContain(UiText.DeliveryReceived, alice.Texts());

        await bob.LaunchAsync();
        await Flows.OpenConversationAsync(bob, "Alice");
        await bob.WaitForTextAsync("Sent while Bob's app was closed.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);
        Assert.DoesNotContain(UiText.DeliveryWaiting, alice.Texts());
    }
}
