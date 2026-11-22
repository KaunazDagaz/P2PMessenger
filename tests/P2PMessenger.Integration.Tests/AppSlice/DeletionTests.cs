using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class DeletionTests
{
    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-11")]
    [Trait("NFR", "NFR-06")]
    public async Task ChatDeletedThenAppRestarted_StaysDeletedAndTheContactsAppKeepsItsCopyInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var databasePath = folder.DatabasePath("alice");
        var protector = new TestKeyProtector();
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        Ed25519PublicKey aliceKey;

        await using (var alice = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken))
        {
            aliceKey = alice.Identity.Keys.IdentityKey;
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            alice.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
            await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
            foreach (var text in new[] { "alice 1", "alice 2" })
            {
                Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, text, cancellationToken)).Outcome);
            }

            await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).All(static message => message.Delivery == DeliveryState.Received), cancellationToken);
            Assert.Equal(SendOutcome.Queued, (await bob.Messenger.SendAsync(aliceKey, "bob 1", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Count == 3, cancellationToken);

            Assert.Equal(3, await alice.Messenger.DeleteChatAsync(bobKey, cancellationToken));
            Assert.Empty(alice.Messenger.Chats.MessagesWith(bobKey));
            Assert.Equal(3, bob.Messenger.Chats.MessagesWith(aliceKey).Count);
        }

        await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);
        await using var restarted = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken);

        Assert.Empty(restarted.Messenger.Chats.MessagesWith(bobKey));
        Assert.True(restarted.ContactFor(bob).IsAdded);
        restarted.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
        await Eventually.HoldsAsync(() => restarted.Messenger.Connections.IsConnected(bobKey) && bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);
        var sent = await restarted.Messenger.SendAsync(bobKey, "alice 3", cancellationToken);
        Assert.Equal(3, sent.Message?.Sequence);
        await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(aliceKey).Count == 4, cancellationToken);
        Assert.Equal(
            ["alice 1", "alice 2", "bob 1", "alice 3"],
            bob.Messenger.Chats.MessagesWith(aliceKey).Select(static message => message.Text));
    }

    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-06")]
    public async Task WaitingMessageDeletedThenSenderRestarted_IsNeverSentWhenTheContactIsBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var databasePath = folder.DatabasePath("alice");
        var protector = new TestKeyProtector();
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        Ed25519PublicKey aliceKey;

        await using (var alice = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken))
        {
            aliceKey = alice.Identity.Keys.IdentityKey;
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            alice.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
            await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && alice.Messenger.Connections.IsConnected(bobKey), cancellationToken);
        }

        await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);
        await using (var alice = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken))
        {
            Assert.False(alice.Messenger.Connections.IsConnected(bobKey));
            await alice.Messenger.SendAsync(bobKey, "keep", cancellationToken);
            var dropped = await alice.Messenger.SendAsync(bobKey, "drop", cancellationToken);
            Assert.True(await alice.Messenger.DeleteWaitingAsync(bobKey, dropped.Message!.Id, cancellationToken));
        }

        await using var restarted = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken);
        Assert.Equal("keep", Assert.Single(restarted.Messenger.Chats.MessagesWith(bobKey)).Text);
        var after = await restarted.Messenger.SendAsync(bobKey, "after", cancellationToken);
        Assert.Equal(3, after.Message?.Sequence);
        restarted.Messenger.Connections.SetAddress(bobKey, bob.Endpoint);
        await Eventually.HoldsAsync(
            () => restarted.Messenger.Chats.MessagesWith(bobKey).All(static message => message.Delivery == DeliveryState.Received),
            cancellationToken);
        await Task.Delay(300, cancellationToken);

        Assert.Equal(["keep", "after"], bob.Messenger.Chats.MessagesWith(aliceKey).Select(static message => message.Text));
    }

    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task AllDataDeleted_LeavesNoFileAndTheNextStartCreatesANewIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var databasePath = folder.DatabasePath("alice");
        var protector = new TestKeyProtector();
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        Ed25519PublicKey oldKey;

        var localData = new DatabaseLocalData(databasePath, protector);
        await using (var alice = await AppPeer.StartSavedAsync(localData, "Alice", null, cancellationToken))
        {
            oldKey = alice.Identity.Keys.IdentityKey;
            await alice.ImportAsync(bob, cancellationToken);
            await alice.Messenger.DisposeAsync();
            Assert.True(await localData.DeleteAllAsync(cancellationToken));
            Assert.False(File.Exists(databasePath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(databasePath)!));
        }

        await using var fresh = await AppPeer.StartSavedAsync(databasePath, protector, "Alice", cancellationToken);
        Assert.NotEqual(oldKey, fresh.Identity.Keys.IdentityKey);
        Assert.Empty(fresh.Messenger.Contacts.All);
    }
}
