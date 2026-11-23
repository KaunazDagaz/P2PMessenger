using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Application.Locking;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class PassphraseTests
{
    private const string Passphrase = "lantern orchard pebble";
    private static readonly PassphraseKdf Cheap = new(1024, 1);

    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-20")]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-06")]
    public async Task AppWithPassphrase_StartedAgain_ReceivesNothingUntilUnlockedAndThenGetsWhatWaited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var databasePath = folder.DatabasePath("alice");
        var protector = new TestKeyProtector();
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        Ed25519PublicKey aliceKey;

        await using (var alice = await AppPeer.StartSavedAsync(new DatabaseLocalData(databasePath, protector, Cheap), "Alice", Passphrase, cancellationToken))
        {
            aliceKey = alice.Identity.Keys.IdentityKey;
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);
            Assert.Equal(SendOutcome.Queued, (await alice.Messenger.SendAsync(bobKey, "alice 1", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => bob.Messenger.Chats.MessagesWith(aliceKey).Count == 1, cancellationToken);
        }

        await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);
        var waiting = await bob.Messenger.SendAsync(aliceKey, "bob 1", cancellationToken);
        Assert.Equal(SendOutcome.Queued, waiting.Outcome);

        var localData = new DatabaseLocalData(databasePath, protector, Cheap);
        AppPeer? restarted = null;
        try
        {
            var opened = await localData.OpenAsync(cancellationToken);
            Assert.Equal(LocalDataStatus.PassphraseNeeded, opened.Status);
            Assert.Null(opened.Stores);
            Assert.Equal(LocalDataStatus.WrongPassphrase, (await localData.UnlockAsync("violet harbour 4471 kite", cancellationToken)).Status);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            Assert.Equal(DeliveryState.Waiting, Assert.Single(bob.Messenger.Chats.WaitingFor(aliceKey)).Delivery);
            Assert.Equal(1, protector.UnprotectCalls);

            restarted = await AppPeer.StartSavedAsync(localData, "Someone else", Passphrase, cancellationToken);
            Assert.Equal(aliceKey, restarted.Identity.Keys.IdentityKey);
            Assert.Single(restarted.Messenger.Chats.MessagesWith(bobKey));

            await Eventually.HoldsAsync(() => restarted.Messenger.Chats.MessagesWith(bobKey).Count == 2, cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.Chats.WaitingFor(aliceKey).Count == 0, cancellationToken);
            Assert.Equal(["alice 1", "bob 1"], restarted.Messenger.Chats.MessagesWith(bobKey).Select(static message => message.Text));
        }
        finally
        {
            if (restarted is not null)
            {
                await restarted.DisposeAsync();
            }
            else
            {
                await localData.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = 90_000)]
    [Trait("Story", "US-20")]
    public async Task LockedApp_KeepsReceivingAndSavingMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var databasePath = folder.DatabasePath("alice");
        var protector = new TestKeyProtector();
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        var bobKey = bob.Identity.Keys.IdentityKey;
        var localData = new DatabaseLocalData(databasePath, protector, Cheap);

        await using (var alice = await AppPeer.StartSavedAsync(localData, "Alice", Passphrase, cancellationToken))
        {
            using var appLock = new AppLock(localData, TimeProvider.System);
            await alice.ImportAsync(bob, cancellationToken);
            await bob.ImportAsync(alice, cancellationToken);
            await Eventually.HoldsAsync(() => alice.ContactFor(bob).IsAdded && bob.ContactFor(alice).IsAdded, cancellationToken);

            appLock.LockNow();
            Assert.True(appLock.IsLocked);
            var sent = await bob.Messenger.SendAsync(alice.Identity.Keys.IdentityKey, "while locked", cancellationToken);
            Assert.Equal(SendOutcome.Queued, sent.Outcome);

            await Eventually.HoldsAsync(() => alice.Messenger.Chats.MessagesWith(bobKey).Count == 1, cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.Chats.WaitingFor(alice.Identity.Keys.IdentityKey).Count == 0, cancellationToken);
            Assert.True(appLock.IsLocked);
            Assert.False(await appLock.UnlockAsync("violet harbour 4471 kite", cancellationToken));
            Assert.True(await appLock.UnlockAsync(Passphrase, cancellationToken));
        }

        await using var reopened = new DatabaseLocalData(databasePath, protector, Cheap);
        var result = await reopened.UnlockAsync(Passphrase, cancellationToken);
        Assert.True(result.IsOpen);
        Assert.Equal("while locked", Assert.Single(await result.Stores.Chats.LoadAllAsync(cancellationToken)).Text);
    }
}
