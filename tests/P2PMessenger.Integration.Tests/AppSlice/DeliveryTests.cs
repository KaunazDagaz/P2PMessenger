using System.Diagnostics;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class DeliveryTests
{
    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-03")]
    [Trait("NFR", "NFR-06")]
    [Trait("NFR", "NFR-07")]
    public async Task ContactAwayAndSenderRestarted_WaitingMessagesSurviveAndAreSentOnceWhenTheContactIsBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var aliceProtector = new TestKeyProtector();
        var bobProtector = new TestKeyProtector();
        var alicePath = folder.DatabasePath("alice");
        var bobPath = folder.DatabasePath("bob");

        var alice = await AppPeer.StartSavedAsync(alicePath, aliceProtector, "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(bobPath, bobProtector, "Bob", cancellationToken);
        var aliceKey = alice.Identity.Keys.IdentityKey;
        var bobKey = bob.Identity.Keys.IdentityKey;
        try
        {
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            alice.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
            await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);

            await bob.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
            alice.Messenger.Connections.SetAddress(bobKey, null);
            foreach (var text in new[] { "while you were away 1", "while you were away 2", "while you were away 3" })
            {
                Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, text, cancellationToken)).Outcome);
            }

            var queued = alice.Messenger.Chats.MessagesWith(bobKey);
            Assert.All(queued, static message => Assert.Equal(DeliveryState.Waiting, message.Delivery));
            await alice.DisposeAsync();

            alice = await AppPeer.StartSavedAsync(alicePath, aliceProtector, "Alice", cancellationToken);
            var restored = alice.Messenger.Chats.MessagesWith(bobKey);
            Assert.Equal(queued.Select(Shape), restored.Select(Shape));
            Assert.Equal(3, alice.Messenger.Chats.WaitingFor(bobKey).Count);

            bob = await AppPeer.StartSavedAsync(bobPath, bobProtector, "Bob", cancellationToken);
            Assert.Empty(bob.Messenger.Chats.MessagesWith(aliceKey));
            alice.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
            await Eventually.HoldsAsync(() => alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
            var reachable = Stopwatch.StartNew();
            await Eventually.HoldsAsync(
                () => alice.Messenger.Chats.MessagesWith(bobKey).All(static message => message.Delivery == DeliveryState.Received),
                cancellationToken);

            Assert.True(reachable.Elapsed < TimeSpan.FromSeconds(30), $"Delivered after {reachable.Elapsed}.");
            var atBob = bob.Messenger.Chats.MessagesWith(aliceKey);
            Assert.Equal(["while you were away 1", "while you were away 2", "while you were away 3"], atBob.Select(static message => message.Text));
            Assert.Equal(queued.Select(static message => message.Id), atBob.Select(static message => message.Id));

            await alice.DisposeAsync();
            alice = await AppPeer.StartSavedAsync(alicePath, aliceProtector, "Alice", cancellationToken);
            Assert.All(alice.Messenger.Chats.MessagesWith(bobKey), static message => Assert.Equal(DeliveryState.Received, message.Delivery));
            Assert.Empty(alice.Messenger.Chats.ContactsWithWaitingMessages());
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-06")]
    public async Task SameMessageSentAgainAfterTheReceiverRestarted_IsShownOnceAndAcknowledgedEachTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protector = new TestKeyProtector();
        var bobPath = folder.DatabasePath("bob");
        using var aliceKeys = KeySet.Create();
        Assert.True(DisplayName.TryCreate("Alice", out var aliceName));
        var text = TestContacts.Text(1, "hello once");

        var bob = await AppPeer.StartSavedAsync(bobPath, protector, "Bob", cancellationToken);
        try
        {
            await bob.ImportAsync(ContactCode.Create(aliceKeys, aliceName).ToText(), CodeCheckOutcome.New, cancellationToken);
            using var bobAtAlice = TestContacts.Create(aliceKeys, bob.Identity.Keys);

            await using (var first = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAtAlice, cancellationToken))
            {
                await first.SendAsync(text, cancellationToken);
                Assert.Equal(text.Id, (await first.NextAsync<Acknowledgement>(cancellationToken)).Id);
            }

            Assert.Equal("hello once", Assert.Single(bob.Messenger.Chats.MessagesWith(aliceKeys.IdentityKey)).Text);
            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(bobPath, protector, "Bob", cancellationToken);
            using var bobAgain = TestContacts.Create(aliceKeys, bob.Identity.Keys);

            await using (var second = await RawContact.OpenAsync(bob.Endpoint, aliceKeys, bobAgain, cancellationToken))
            {
                await second.SendAsync(text, cancellationToken);
                Assert.Equal(text.Id, (await second.NextAsync<Acknowledgement>(cancellationToken)).Id);
                await second.SendAsync(text, cancellationToken);
                Assert.Equal(text.Id, (await second.NextAsync<Acknowledgement>(cancellationToken)).Id);
            }

            var shown = Assert.Single(bob.Messenger.Chats.MessagesWith(aliceKeys.IdentityKey));
            Assert.Equal(text.Id, shown.Id);
            Assert.Equal(0, bob.Messenger.Chats.UnreadCount(aliceKeys.IdentityKey));
        }
        finally
        {
            await bob.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    [Trait("NFR", "NFR-03")]
    [Trait("NFR", "NFR-07")]
    [Trait("NFR", "NFR-09")]
    public async Task WithoutTheContactsAcknowledgement_MessageStaysWaitingAndIsSentAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wakeUps = new ManualWakeUps();
        var resendAfter = TimeSpan.FromSeconds(2);
        using var bobKeys = KeySet.Create();
        using var carolKeys = KeySet.Create();
        using var bobServer = new RawServer();
        using var carolServer = new RawServer();
        await using var alice = await AppPeer.StartAsync(
            KeySet.Create(),
            "Alice",
            setup => setup with { WakeUps = wakeUps, Delivery = new DeliveryOptions { ResendAfter = resendAfter } },
            cancellationToken);
        Assert.True(DisplayName.TryCreate("Bob", out var bobName));
        Assert.True(DisplayName.TryCreate("Carol", out var carolName));
        await alice.ImportAsync(ContactCode.Create(bobKeys, bobName).ToText(), CodeCheckOutcome.New, cancellationToken);
        await alice.ImportAsync(ContactCode.Create(carolKeys, carolName).ToText(), CodeCheckOutcome.New, cancellationToken);
        using var aliceAtBob = TestContacts.Create(bobKeys, alice.Identity.Keys);
        using var aliceAtCarol = TestContacts.Create(carolKeys, alice.Identity.Keys);

        alice.Messenger.Connections.SetAddress(bobKeys.IdentityKey, bobServer.Endpoint);
        alice.Messenger.Connections.SetAddress(carolKeys.IdentityKey, carolServer.Endpoint);
        await using var bob = await RawContact.AcceptAsync(bobServer, bobKeys, aliceAtBob, cancellationToken);
        await using var carol = await RawContact.AcceptAsync(carolServer, carolKeys, aliceAtCarol, cancellationToken);
        await Eventually.HoldsAsync(() => alice.ContactFor(bobKeys).IsAdded && alice.ContactFor(carolKeys).IsAdded, cancellationToken);

        var sent = await alice.Messenger.SendAsync(bobKeys.IdentityKey, "did you get this?", cancellationToken);
        Assert.True(sent.IsQueued);
        var first = await bob.NextAsync<TextMessage>(cancellationToken);
        Assert.Equal(sent.Message.Id, first.Id);
        Assert.Equal(1, first.Sequence);

        var fromBob = TestContacts.Text(1, "from Bob");
        await bob.SendAsync(Acknowledgement.Create(MessageId.Create()), cancellationToken);
        await bob.SendAsync(fromBob, cancellationToken);
        Assert.Equal(fromBob.Id, (await bob.NextAsync<Acknowledgement>(cancellationToken)).Id);
        var fromCarol = TestContacts.Text(1, "from Carol");
        await carol.SendAsync(Acknowledgement.Create(first.Id), cancellationToken);
        await carol.SendAsync(fromCarol, cancellationToken);
        Assert.Equal(fromCarol.Id, (await carol.NextAsync<Acknowledgement>(cancellationToken)).Id);
        Assert.Equal(DeliveryState.Waiting, SentTo(alice, bobKeys).Delivery);

        wakeUps.Fire();
        Assert.Null(await bob.NextWithinAsync(TimeSpan.FromMilliseconds(200), cancellationToken));
        await Task.Delay(resendAfter, cancellationToken);
        wakeUps.Fire();
        var again = await bob.NextAsync<TextMessage>(cancellationToken);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Sequence, again.Sequence);
        Assert.Equal(first.Text, again.Text);
        Assert.Equal(DeliveryState.Waiting, SentTo(alice, bobKeys).Delivery);

        await bob.DisposeAsync();
        await using var bobAgain = await RawContact.AcceptAsync(bobServer, bobKeys, aliceAtBob, cancellationToken);
        var afterReconnect = await bobAgain.NextAsync<TextMessage>(cancellationToken);
        Assert.Equal(first.Id, afterReconnect.Id);
        Assert.Equal(DeliveryState.Waiting, SentTo(alice, bobKeys).Delivery);

        await bobAgain.SendAsync(Acknowledgement.Create(first.Id), cancellationToken);

        await Eventually.HoldsAsync(() => SentTo(alice, bobKeys).Delivery == DeliveryState.Received, cancellationToken);
        await Task.Delay(resendAfter, cancellationToken);
        wakeUps.Fire();
        Assert.Null(await bobAgain.NextWithinAsync(TimeSpan.FromMilliseconds(300), cancellationToken));
    }

    private static ChatMessage SentTo(AppPeer peer, KeySet contact) =>
        peer.Messenger.Chats.MessagesWith(contact.IdentityKey).Single(static message => message.Direction == MessageDirection.Outgoing);

    private static (MessageId, MessageDirection, long, DateTimeOffset, string, DateTimeOffset, DeliveryState) Shape(ChatMessage message) =>
        (message.Id, message.Direction, message.Sequence, message.WrittenAt, message.Text, message.RecordedAt, message.Delivery);
}
