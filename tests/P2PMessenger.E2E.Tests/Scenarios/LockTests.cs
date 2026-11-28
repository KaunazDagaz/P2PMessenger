using Avalonia.Headless.XUnit;
using P2PMessenger.E2E.Tests.Support;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.E2E.Tests.Scenarios;

public sealed class LockTests
{
    [AvaloniaFact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Lock_WithThePassphraseSet_HidesTheChatKeepsReceivingAndOpensOnlyWithTheRightPassphrase()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob, Flows.Passphrase);
        await Flows.SendAsync(alice, "Written before the lock.");
        await alice.WaitForTextAsync(UiText.DeliveryReceived);

        alice.ClickButton("LockButton");
        await alice.WaitForTitleAsync(UiText.UnlockTitle);
        Assert.NotNull(alice.TryNamed<Avalonia.Controls.TextBox>("PassphraseBox"));
        Assert.False(alice.Shows("Written before the lock."));

        await Flows.SendAsync(bob, "Arrives while Alice is locked.");
        await bob.WaitForTextAsync(UiText.DeliveryReceived);
        Assert.False(alice.Shows("Arrives while Alice is locked."));

        alice.Type("PassphraseBox", "not the passphrase");
        alice.ClickButton("UnlockButton");
        await alice.WaitForTextAsync(UiText.UnlockWrong);
        Assert.Equal(UiText.UnlockTitle, alice.Title);
        Assert.False(alice.Shows("Written before the lock."));

        alice.Clear("PassphraseBox");
        alice.Type("PassphraseBox", Flows.Passphrase);
        alice.ClickButton("UnlockButton");
        await alice.WaitForTitleAsync("Bob");
        await alice.WaitForTextAsync("Written before the lock.");
        await alice.WaitForTextAsync("Arrives while Alice is locked.");
        Assert.False(await alice.DatabaseFilesContainAsync("Arrives while Alice is locked."));
        Assert.False(await alice.DatabaseFilesContainAsync("Written before the lock."));
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-06")]
    public async Task Restart_WithThePassphraseSet_ReceivesNothingUntilTheRightPassphraseIsEntered()
    {
        LoopbackDiscovery hub = new();
        await using Device alice = new(hub, "Alice");
        await using Device bob = new(hub, "Bob");
        await Flows.SetUpPairAsync(alice, bob, Flows.Passphrase);

        await alice.QuitAsync();
        await bob.WaitForTextAsync(UiText.ContactNotReachable);
        await Flows.SendAsync(bob, "Sent while Alice's app is closed.");
        await alice.LaunchAsync();

        await alice.WaitForTitleAsync(UiText.UnlockTitle);
        Assert.NotNull(alice.TryNamed<Avalonia.Controls.TextBox>("PassphraseBox"));
        Assert.Null(alice.TryNamed<Avalonia.Controls.ItemsControl>("ChatItems"));
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(bob.Shows(UiText.DeliveryWaiting));
        Assert.False(bob.Shows(UiText.ContactReachableNearby));

        alice.Type("PassphraseBox", Flows.Passphrase);
        alice.ClickButton("UnlockButton");
        await alice.WaitForTitleAsync(UiText.ContactListTitle);
        await bob.WaitUntilAsync(() => !bob.Shows(UiText.DeliveryWaiting), "Bob's message is no longer waiting once Alice has unlocked");
        Assert.True(bob.Shows(UiText.DeliveryReceived));

        await Flows.OpenConversationAsync(alice, "Bob");
        await alice.WaitForTextAsync("Sent while Alice's app is closed.");
    }
}
