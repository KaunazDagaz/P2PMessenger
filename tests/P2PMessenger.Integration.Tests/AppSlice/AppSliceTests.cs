using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class AppSliceTests
{
    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-03")]
    [Trait("Story", "US-06")]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-11")]
    public async Task TextCodes_BothImportedOnOneNetwork_FindEachOtherWithoutAnAddressAddEachOtherAndChat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await alice.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);

        await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);
        await Eventually.HoldsAsync(
            () => alice.Messenger.Connections.IsConnected(bob.Identity.Keys.IdentityKey) && bob.Messenger.Connections.IsConnected(alice.Identity.Keys.IdentityKey),
            cancellationToken);
        Assert.Equal(KeyComparison.NotCompared, alice.ContactFor(bob).KeyComparison);
        Assert.Equal(KeyComparison.NotCompared, bob.ContactFor(alice).KeyComparison);
        Assert.Contains(alice.Changes, static change => change.Change == ContactChange.FirstConnection);
        Assert.Contains(bob.Changes, static change => change.Change == ContactChange.FirstConnection);
        await Eventually.HoldsAsync(() => alice.Messenger.Connections.DialStatusOf(bob.Identity.Keys.IdentityKey) == DialStatus.None, cancellationToken);
        Assert.Equal(Reachability.Nearby, alice.Messenger.Connections.ReachabilityOf(bob.Identity.Keys.IdentityKey));
        Assert.Equal(Reachability.Nearby, bob.Messenger.Connections.ReachabilityOf(alice.Identity.Keys.IdentityKey));

        var bobKey = bob.Identity.Keys.IdentityKey;
        var aliceKey = alice.Identity.Keys.IdentityKey;
        for (int index = 1; index <= 5; index++)
        {
            var sent = await alice.Messenger.SendAsync(bobKey, $"alice {index}", cancellationToken);
            Assert.Equal(SendOutcome.Queued, sent.Outcome);
            Assert.Equal(index, sent.Message?.Sequence);
        }

        Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKey, "bob 1", cancellationToken)).Outcome);
        Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKey, "bob 2", cancellationToken)).Outcome);

        await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(aliceKey).Count == 7, cancellationToken);
        await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Count == 7, cancellationToken);
        var atBob = bob.Messenger.Chats.MessagesWith(aliceKey);
        Assert.Equal(
            ["alice 1", "alice 2", "alice 3", "alice 4", "alice 5"],
            atBob.Where(static message => message.Direction == MessageDirection.Incoming).Select(static message => message.Text));
        Assert.Equal(
            ["bob 1", "bob 2"],
            atBob.Where(static message => message.Direction == MessageDirection.Outgoing).Select(static message => message.Text));
        var atAlice = alice.Messenger.Chats.MessagesWith(bobKey);
        Assert.Equal(
            atAlice.Where(static message => message.Direction == MessageDirection.Outgoing).Select(static message => (message.Id, message.WrittenAt, message.Text)),
            atBob.Where(static message => message.Direction == MessageDirection.Incoming).Select(static message => (message.Id, message.WrittenAt, message.Text)));
        Assert.Equal(2, alice.Messenger.Chats.UnreadCount(bobKey));
        Assert.Equal(5, bob.Messenger.Chats.UnreadCount(aliceKey));
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-05")]
    public async Task CodeImportedOnOneSideOnly_StaysWaitingUntilTheOtherSideImportsToo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        await alice.ImportAsync(bob, cancellationToken);

        await Eventually.HoldsAsync(() => AppPeer.HubOfThisTest.Sent.Count >= 10, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.False(alice.Messenger.Connections.IsHeard(bobKey));
        Assert.Equal(DialStatus.None, alice.Messenger.Connections.DialStatusOf(bobKey));
        Assert.False(alice.ContactFor(bob).IsAdded);
        Assert.Empty(bob.Messenger.Contacts.All);
        Assert.Empty(bob.Changes);
        Assert.Empty(bob.Messenger.Chats.MessagesWith(alice.Identity.Keys.IdentityKey));
        Assert.Equal(SendOutcome.WaitingForFirstConnection, (await alice.Messenger.SendAsync(bobKey, "hello", cancellationToken)).Outcome);

        await bob.ImportAsync(alice, cancellationToken);

        await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task SwappedCode_TheRealContactIsNeverRecognizedAndNothingIsAdded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        using var mallory = KeySet.Create();
        Assert.True(DisplayName.TryCreate("Bob", out var bobsName));
        var swapped = await alice.ImportAsync(ContactCode.Create(mallory, bobsName).ToText(), CodeCheckOutcome.New, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);

        await Eventually.HoldsAsync(() => AppPeer.HubOfThisTest.Sent.Count >= 10, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.False(alice.Messenger.Connections.IsHeard(swapped.IdentityKey));
        Assert.False(bob.Messenger.Connections.IsHeard(alice.Identity.Keys.IdentityKey));
        Assert.Equal(DialStatus.None, alice.Messenger.Connections.DialStatusOf(swapped.IdentityKey));
        Assert.False(alice.ContactFor(mallory.IdentityKey).IsAdded);
        Assert.False(bob.ContactFor(alice).IsAdded);
        Assert.DoesNotContain(alice.Changes, static change => change.Change == ContactChange.FirstConnection);
        Assert.DoesNotContain(bob.Changes, static change => change.Change == ContactChange.FirstConnection);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-09")]
    public async Task ContactWithANewIdentity_IsNotRecognizedUntilItsCodeIsAddedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        var oldBob = await AppPeer.StartAsync("Bob", cancellationToken);
        var oldBobKey = oldBob.Identity.Keys.IdentityKey;
        await alice.ImportAsync(oldBob, cancellationToken);
        await oldBob.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(oldBobKey).IsAdded, cancellationToken);
        await oldBob.DisposeAsync();

        await using var newBob = await AppPeer.StartAsync("Bob", cancellationToken);
        await newBob.ImportAsync(alice, cancellationToken);

        var sentBefore = AppPeer.HubOfThisTest.Sent.Count;
        await Eventually.HoldsAsync(() => AppPeer.HubOfThisTest.Sent.Count >= sentBefore + 10, cancellationToken);
        await Task.Delay(300, cancellationToken);
        Assert.False(alice.Messenger.Connections.IsHeard(newBob.Identity.Keys.IdentityKey));
        Assert.False(newBob.Messenger.Connections.IsHeard(alice.Identity.Keys.IdentityKey));
        Assert.Equal(DialStatus.None, newBob.Messenger.Connections.DialStatusOf(alice.Identity.Keys.IdentityKey));
        Assert.Single(alice.Messenger.Contacts.All);
        Assert.Empty(alice.Messenger.Chats.MessagesWith(newBob.Identity.Keys.IdentityKey));
        Assert.False(alice.Messenger.Connections.IsConnected(newBob.Identity.Keys.IdentityKey));

        var check = alice.Messenger.Contacts.Check(newBob.Identity.CodeText);
        Assert.Equal(CodeCheckOutcome.LooksLikeContact, check.Outcome);
        Assert.True((await alice.Messenger.Contacts.AddAsync(check, cancellationToken)).IsSaved);

        await Eventually.HoldsAsync(() => alice.ContactFor(newBob).IsAdded && newBob.ContactFor(alice).IsAdded, cancellationToken);
        Assert.True(alice.ContactFor(oldBobKey).IsAdded);
        Assert.Equal(2, alice.Messenger.Contacts.All.Count);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-05")]
    public async Task NewConnectionKeysAccepted_TheContactConnectsWithTheNewKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        var bobKeys = KeySet.Create();
        var bob = await AppPeer.StartAsync(bobKeys, "Bob", cancellationToken);
        await alice.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(bobKeys).IsAdded, cancellationToken);
        var renewed = RenewedKeys.WithNewStaticKey(bobKeys);
        await bob.DisposeAsync();
        await using var renewedBob = await AppPeer.StartAsync(renewed, "Bob", cancellationToken);
        await renewedBob.ImportAsync(alice, cancellationToken);

        var check = alice.Messenger.Contacts.Check(renewedBob.Identity.CodeText);
        var result = await alice.Messenger.Contacts.AddAsync(check, cancellationToken);

        Assert.Equal(CodeCheckOutcome.NewConnectionKeys, check.Outcome);
        Assert.Equal(AddContactOutcome.KeysReplaced, result.Outcome);
        Assert.Equal(ContactState.WaitingForFirstConnection, result.Contact?.State);
        await Eventually.HoldsAsync(() => alice.ContactFor(renewed).IsAdded && alice.Messenger.Connections.IsConnected(renewed.IdentityKey), cancellationToken);
        Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(renewed.IdentityKey, "still you?", cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => renewedBob.Messenger.Chats.MessagesWith(alice.Identity.Keys.IdentityKey).Count == 1, cancellationToken);
    }

    [Fact(Timeout = 60_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-07")]
    public async Task ContactGoesAway_MessageIsShownAndKeptAsWaiting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        await alice.ImportAsync(bob, cancellationToken);
        await bob.ImportAsync(alice, cancellationToken);
        await Eventually.HoldsAsync(() => alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);

        await bob.DisposeAsync();

        await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
        var result = await alice.Messenger.SendAsync(bobKey, "are you there?", cancellationToken);
        Assert.Equal(SendOutcome.Queued, result.Outcome);
        await Task.Delay(300, cancellationToken);
        var shown = Assert.Single(alice.Messenger.Chats.MessagesWith(bobKey));
        Assert.Equal("are you there?", shown.Text);
        Assert.Equal(DeliveryState.Waiting, shown.Delivery);
        Assert.True(alice.ContactFor(bobKey).IsAdded);
    }
}
