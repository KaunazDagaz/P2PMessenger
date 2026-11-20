using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class RepositoryTests
{
    private const int ConstraintFailed = 19;

    [Fact]
    [Trait("Story", "US-01")]
    [Trait("Story", "US-20")]
    public async Task Identity_CreatedThenLoaded_HasTheSameKeysAndName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var keys = KeySet.Create();
        Assert.Null(await store.Data.Identity.LoadAsync(cancellationToken));

        Assert.True(await store.Data.Identity.TryCreateAsync(keys, Samples.Name("Alice"), cancellationToken));
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = await store.Data.Identity.LoadAsync(cancellationToken);

        Assert.NotNull(loaded);
        using var loadedKeys = loaded.Keys;
        Assert.Equal("Alice", loaded.Name.Value);
        Assert.Equal(Export(keys), Export(loadedKeys));
        Assert.Equal(keys.Fingerprint, loadedKeys.Fingerprint);
    }

    [Fact]
    [Trait("Story", "US-01")]
    public async Task Identity_CreatedTwice_KeepsTheFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var first = KeySet.Create();
        using var second = KeySet.Create();

        Assert.True(await store.Data.Identity.TryCreateAsync(first, Samples.Name("Alice"), cancellationToken));
        Assert.False(await store.Data.Identity.TryCreateAsync(second, Samples.Name("Mallory"), cancellationToken));

        var loaded = await store.Data.Identity.LoadAsync(cancellationToken);
        using var loadedKeys = loaded!.Keys;
        Assert.Equal(first.IdentityKey, loadedKeys.IdentityKey);
        Assert.Equal("Alice", loaded.Name.Value);
    }

    [Fact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public async Task Contact_SavedThenLoaded_RebuildsTheSameCodeAndKeepsItsState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        var bobContact = new StoredContact(Samples.Code(bob, "Bob"), ContactChannel.InPerson, StoredContactStatus.Confirmed, KeyChecked: true);
        var carolContact = Samples.Contact(carol, "Карина Ж.");

        await store.Data.Contacts.SaveAsync(bobContact, cancellationToken);
        await store.Data.Contacts.SaveAsync(carolContact, cancellationToken);
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = await store.Data.Contacts.LoadAllAsync(cancellationToken);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(bobContact.Code.ToBytes(), loaded[0].Code.ToBytes());
        Assert.Equal(ContactChannel.InPerson, loaded[0].AddedVia);
        Assert.Equal(StoredContactStatus.Confirmed, loaded[0].Status);
        Assert.True(loaded[0].KeyChecked);
        Assert.Equal(carolContact.Code.ToBytes(), loaded[1].Code.ToBytes());
        Assert.Equal("Карина Ж.", loaded[1].Code.DisplayName.Value);
        Assert.Equal(StoredContactStatus.WaitingForFirstConnection, loaded[1].Status);
        Assert.False(loaded[1].KeyChecked);
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task Contact_SavedAgainWithNewConnectionKeysAndState_UpdatesTheOnePerson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var bob = KeySet.Create();
        using var renewed = RenewedKeys(bob);
        await store.Data.Contacts.SaveAsync(Samples.Contact(bob, "Bob"), cancellationToken);

        var updated = Samples.Contact(renewed, "Robert", StoredContactStatus.Confirmed, keyChecked: true);
        await store.Data.Contacts.SaveAsync(updated, cancellationToken);
        var loaded = Assert.Single(await store.Data.Contacts.LoadAllAsync(cancellationToken));

        Assert.Equal(updated.Code.ToBytes(), loaded.Code.ToBytes());
        Assert.Equal(StoredContactStatus.Confirmed, loaded.Status);
        Assert.True(loaded.KeyChecked);
        await using var raw = await store.RawAsync(cancellationToken);
        Assert.Equal(1, await Sql.ScalarAsync(raw, "SELECT count(*) FROM person;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-02")]
    public async Task Contact_WithTheUsersOwnIdentityKey_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var user = KeySet.Create();
        await store.Data.Identity.TryCreateAsync(user, Samples.Name("Alice"), cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Data.Contacts.SaveAsync(Samples.Contact(user, "Alice"), cancellationToken));
        Assert.Empty(await store.Data.Contacts.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-05")]
    public async Task Contact_WithTheStaticKeyOfAnotherContact_IsRefusedByTheDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var bob = KeySet.Create();
        using var copy = SameStaticKey(bob);
        await store.Data.Contacts.SaveAsync(Samples.Contact(bob, "Bob"), cancellationToken);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => store.Data.Contacts.SaveAsync(Samples.Contact(copy, "Mallory"), cancellationToken));

        Assert.Equal(ConstraintFailed, exception.SqliteErrorCode);
        Assert.Single(await store.Data.Contacts.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-11")]
    [Trait("Story", "US-15")]
    public async Task Messages_AddedThenLoaded_KeepEveryFieldInArrivalOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var added = Samples.Message(bob.IdentityKey, StoredMessageDirection.Outgoing, 2, "Second ✓ line\nwith a break", 5);
        await store.Data.Messages.AddAsync(added, cancellationToken);

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = await store.Data.Messages.LoadAllAsync(cancellationToken);

        Assert.Equal(["Hello Bob", "Hi Alice", "Carol here", added.Text], loaded.Select(static message => message.Text));
        Assert.Equal(
            [StoredMessageDirection.Outgoing, StoredMessageDirection.Incoming, StoredMessageDirection.Incoming, StoredMessageDirection.Outgoing],
            loaded.Select(static message => message.Direction));
        Assert.Equal([bob.IdentityKey, bob.IdentityKey, carol.IdentityKey, bob.IdentityKey], loaded.Select(static message => message.ContactKey));
        Assert.Equal(added, loaded[3]);
    }

    [Fact]
    public async Task Message_ForAPersonWhoIsNotAContact_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var user = KeySet.Create();
        using var stranger = KeySet.Create();
        await store.Data.Identity.TryCreateAsync(user, Samples.Name("Alice"), cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.Data.Messages.AddAsync(Samples.Message(stranger.IdentityKey, StoredMessageDirection.Incoming, 1, "Hello", 0), cancellationToken));
        Assert.Empty(await store.Data.Messages.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-15")]
    public async Task Message_WithAnIdAlreadyStored_IsRefusedByTheDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var first = (await store.Data.Messages.LoadAllAsync(cancellationToken))[0];

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => store.Data.Messages.AddAsync(first with { Sequence = 9, Text = "Again" }, cancellationToken));

        Assert.Equal(ConstraintFailed, exception.SqliteErrorCode);
        Assert.Equal(3, (await store.Data.Messages.LoadAllAsync(cancellationToken)).Count);
    }

    [Fact]
    [Trait("Story", "US-05")]
    [Trait("Story", "US-15")]
    public async Task SeenIds_AddedOnceOrTwice_AreFoundAfterAReopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var seen = MessageId.Create();
        var other = MessageId.Create();

        await store.Data.SeenMessages.AddAsync(seen, cancellationToken);
        await store.Data.SeenMessages.AddAsync(seen, cancellationToken);
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);

        Assert.True(await store.Data.SeenMessages.ContainsAsync(seen, cancellationToken));
        Assert.False(await store.Data.SeenMessages.ContainsAsync(other, cancellationToken));
        await using var raw = await store.RawAsync(cancellationToken);
        Assert.Equal(1, await Sql.ScalarAsync(raw, "SELECT count(*) FROM seen_message;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task StoredData_UnderAnotherDataKey_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;

        await store.ReopenAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Identity.LoadAsync(cancellationToken));
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Contacts.LoadAllAsync(cancellationToken));
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Messages.LoadAllAsync(cancellationToken));
    }

    private static byte[] Export(KeySet keys)
    {
        var exported = new byte[KeySet.ExportLength];
        Assert.True(keys.TryExport(exported, out _));
        return exported;
    }

    private static KeySet RenewedKeys(KeySet keys)
    {
        var exported = Export(keys);
        RandomNumberGenerator.Fill(exported.AsSpan(1 + 32, 32));
        RandomNumberGenerator.Fill(exported.AsSpan(1 + 32 + 32, 32));
        Assert.True(KeySet.TryImport(exported, out var renewed));
        CryptographicOperations.ZeroMemory(exported);
        return renewed;
    }

    private static KeySet SameStaticKey(KeySet keys)
    {
        var exported = Export(keys);
        RandomNumberGenerator.Fill(exported.AsSpan(1, 32));
        RandomNumberGenerator.Fill(exported.AsSpan(1 + 32 + 32, 32));
        Assert.True(KeySet.TryImport(exported, out var copy));
        CryptographicOperations.ZeroMemory(exported);
        return copy;
    }
}
