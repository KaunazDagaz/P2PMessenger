using System.Globalization;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class RestartTests
{
    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-20")]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-05")]
    [Trait("NFR", "NFR-06")]
    public async Task SavedApp_ClosedAndStartedAgain_KeepsItsIdentityContactAndChatAndChatsOn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-restart-test-{Guid.NewGuid():N}"));
        var databasePath = Path.Combine(folder, "messenger.db");
        var protector = new TestKeyProtector();
        try
        {
            await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
            var bobKey = bob.Identity.Keys.IdentityKey;
            IReadOnlyList<ChatMessage> before;
            Ed25519PublicKey aliceKey;
            string aliceCode;

            await using (var alice = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken))
            {
                aliceKey = alice.Identity.Keys.IdentityKey;
                aliceCode = alice.Identity.CodeText;
                await alice.ImportAsync(bob, cancellationToken);
                await bob.ImportAsync(alice, cancellationToken);
                alice.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
                await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
                Assert.True(await alice.Messenger.Contacts.SetKeyComparisonAsync(bobKey, KeyComparison.CheckedByYou, cancellationToken));

                Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, "alice 1", cancellationToken)).Outcome);
                await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(aliceKey).Count == 1, cancellationToken);
                Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKey, "bob 1", cancellationToken)).Outcome);
                await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Count == 2, cancellationToken);
                Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, "alice 2", cancellationToken)).Outcome);
                await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Count == 3, cancellationToken);
                before = alice.Messenger.Chats.MessagesWith(bobKey);
            }

            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            await using var restarted = await AppPeer.StartSavedAsync(databasePath, protector, "Someone else", cancellationToken);
            Assert.Equal(aliceKey, restarted.Identity.Keys.IdentityKey);
            Assert.Equal("Alice", restarted.Identity.Name.Value);
            Assert.Equal(aliceCode, restarted.Identity.CodeText);
            var contact = restarted.ContactFor(bob);
            Assert.True(contact.IsAdded);
            Assert.Equal(KeyComparison.CheckedByYou, contact.KeyComparison);
            Assert.Equal(bob.Identity.Code, contact.Code);
            Assert.Equal(before.Select(Shape), restarted.Messenger.Chats.MessagesWith(bobKey).Select(Shape));

            var received = before.First(static message => message.Direction == MessageDirection.Incoming);
            Assert.True(TextMessage.TryCreate(received.Id, received.Sequence, received.WrittenAt, received.Text, out var replay));
            Assert.False(await restarted.Messenger.Chats.ReceiveAsync(bobKey, replay, cancellationToken));

            restarted.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
            await Eventually.HoldsAsync(() => restarted.Messenger.Connections.IsConnected(bobKey) && bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);
            var sent = await restarted.Messenger.SendAsync(bobKey, "alice 3", cancellationToken);
            Assert.Equal(SendOutcome.Queued, sent.Outcome);
            Assert.Equal(3, sent.Message?.Sequence);
            await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(aliceKey).Count == 4, cancellationToken);
            Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKey, "bob 2", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => restarted.Messenger.Chats.MessagesWith(bobKey).Count == 5, cancellationToken);
            Assert.Equal(
                ["alice 1", "bob 1", "alice 2", "alice 3", "bob 2"],
                restarted.Messenger.Chats.MessagesWith(bobKey).Select(static message => message.Text));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static (MessageId, MessageDirection, long, DateTimeOffset, string, DateTimeOffset) Shape(ChatMessage message) =>
        (message.Id, message.Direction, message.Sequence, message.WrittenAt, message.Text, message.RecordedAt);
}
