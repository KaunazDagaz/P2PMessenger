using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class DeletionTests
{
    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    public async Task DeleteConversation_RemovesItsMessagesAndWaitingDeliveriesOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var before = await store.Data.Messages.LoadAllAsync(cancellationToken);

        var deleted = await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);

        Assert.Equal(2, deleted);
        var left = Assert.Single(await store.Data.Messages.LoadAllAsync(cancellationToken));
        Assert.Equal(before[2], left);
        Assert.Equal(2, (await store.Data.Contacts.LoadAllAsync(cancellationToken)).Count);
        await using var raw = await store.RawAsync(cancellationToken);
        Assert.Equal(0, await Sql.ScalarAsync(raw, "SELECT count(*) FROM outbox;", cancellationToken));
        Assert.Equal(0, await Sql.ScalarAsync(raw, "SELECT count(*) FROM message_delivery;", cancellationToken));
        Assert.Equal(2, await Sql.ScalarAsync(raw, "SELECT count(*) FROM conversation;", cancellationToken));
        Assert.Empty(await Sql.StringsAsync(raw, "PRAGMA foreign_key_check;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-05")]
    public async Task DeleteConversation_KeepsTheIdsOfItsMessagesAsSeen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var bobs = (await store.Data.Messages.LoadAllAsync(cancellationToken)).Take(2).ToList();

        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);

        foreach (var message in bobs)
        {
            Assert.True(await store.Data.SeenMessages.ContainsAsync(message.Id, cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-11")]
    public async Task DeleteConversation_KeepsTheHighestSentSequenceAcrossAReopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.Data.Messages.AddAsync(Samples.Message(bob.IdentityKey, StoredMessageDirection.Outgoing, 2, "Second", 3), cancellationToken);
        Assert.Empty(await store.Data.Messages.LoadSentSequencesAsync(cancellationToken));

        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);

        var sequences = await store.Data.Messages.LoadSentSequencesAsync(cancellationToken);
        Assert.Equal(2, Assert.Single(sequences, pair => pair.Key.Equals(bob.IdentityKey)).Value);
        Assert.Single(sequences);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-11")]
    public async Task DeleteConversation_AgainWithLowerNumbers_KeepsTheHigherSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.Data.Messages.AddAsync(Samples.Message(bob.IdentityKey, StoredMessageDirection.Outgoing, 5, "Fifth", 3), cancellationToken);
        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await store.Data.Messages.AddAsync(Samples.Message(bob.IdentityKey, StoredMessageDirection.Incoming, 9, "From Bob", 4), cancellationToken);

        Assert.Equal(1, await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken));

        Assert.Equal(5, (await store.Data.Messages.LoadSentSequencesAsync(cancellationToken))[bob.IdentityKey]);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteConversation_StoresTheSequenceEncryptedAndBoundToItsChat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);

        var stored = await ReadSequenceAsync(raw, cancellationToken);

        Assert.Equal(48, stored.Length);
        var plain = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(plain, 1);
        Assert.Equal(-1, stored.AsSpan().IndexOf(plain));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    public async Task LoadSentSequences_EveryFlippedBitOfTheStoredSequence_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);
        var original = await ReadSequenceAsync(raw, cancellationToken);

        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            await Sql.ExecuteAsync(raw, "UPDATE conversation SET sent_sequence_enc = $value WHERE sent_sequence_enc IS NOT NULL;", [("$value", changed)], cancellationToken);

            await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Messages.LoadSentSequencesAsync(cancellationToken));
        }

        await Sql.ExecuteAsync(raw, "UPDATE conversation SET sent_sequence_enc = $value WHERE sent_sequence_enc IS NOT NULL;", [("$value", original)], cancellationToken);
        Assert.Single(await store.Data.Messages.LoadSentSequencesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    public async Task LoadSentSequences_SequenceMovedToAnotherChat_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);

        await Sql.ExecuteAsync(
            raw,
            "UPDATE conversation SET sent_sequence_enc = (SELECT sent_sequence_enc FROM conversation WHERE sent_sequence_enc IS NOT NULL) WHERE sent_sequence_enc IS NULL;",
            [],
            cancellationToken);
        await Sql.ExecuteAsync(raw, "UPDATE conversation SET sent_sequence_enc = NULL WHERE contact_person_id = 2;", [], cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Messages.LoadSentSequencesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteConversation_ContactWithoutAChatOrUnknownKey_DeletesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        using var dave = KeySet.Create();
        using var stranger = KeySet.Create();
        await store.Data.Contacts.SaveAsync(Samples.Contact(dave, "Dave"), cancellationToken);
        var before = await store.Data.Messages.LoadAllAsync(cancellationToken);

        Assert.Equal(0, await store.Data.Messages.DeleteConversationAsync(dave.IdentityKey, cancellationToken));
        Assert.Equal(0, await store.Data.Messages.DeleteConversationAsync(stranger.IdentityKey, cancellationToken));
        Assert.Equal(0, await store.Data.Messages.DeleteConversationAsync(user.IdentityKey, cancellationToken));

        Assert.Equal(before, await store.Data.Messages.LoadAllAsync(cancellationToken));
        Assert.Empty(await store.Data.Messages.LoadSentSequencesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteConversation_LeavesNoStoredTextOfItsMessagesInTheFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        List<byte[]> deletedTexts;
        await using (var raw = await store.RawAsync(cancellationToken))
        {
            deletedTexts = await BlobsAsync(raw, "SELECT text_enc FROM message WHERE conversation_id = (SELECT id FROM conversation WHERE contact_person_id = 2);", cancellationToken);
        }

        Assert.Equal(2, deletedTexts.Count);
        await store.Data.Messages.DeleteConversationAsync(bob.IdentityKey, cancellationToken);

        foreach (var path in DatabaseFiles(store.FilePath))
        {
            var content = await ReadSharedAsync(path, cancellationToken);
            foreach (var text in deletedTexts)
            {
                Assert.Equal(-1, content.AsSpan().IndexOf(text));
            }
        }
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaiting_OwnWaitingMessage_IsRemovedWithItsDeliveryAndKeepsItsNumber()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var second = Samples.Message(bob.IdentityKey, StoredMessageDirection.Outgoing, 2, "Second", 3);
        await store.Data.Messages.AddAsync(second, cancellationToken);

        Assert.True(await store.Data.Messages.DeleteWaitingAsync(bob.IdentityKey, second.Id, cancellationToken));
        Assert.False(await store.Data.Messages.DeleteWaitingAsync(bob.IdentityKey, second.Id, cancellationToken));
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);

        Assert.DoesNotContain(await store.Data.Messages.LoadAllAsync(cancellationToken), message => message.Id.Equals(second.Id));
        Assert.Equal(3, (await store.Data.Messages.LoadAllAsync(cancellationToken)).Count);
        Assert.Equal(2, (await store.Data.Messages.LoadSentSequencesAsync(cancellationToken))[bob.IdentityKey]);
        Assert.True(await store.Data.SeenMessages.ContainsAsync(second.Id, cancellationToken));
        await using var raw = await store.RawAsync(cancellationToken);
        Assert.Equal(1, await Sql.ScalarAsync(raw, "SELECT count(*) FROM outbox;", cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("NFR", "NFR-07")]
    public async Task DeleteWaiting_ReceivedIncomingOrAnotherContactsMessage_IsNotDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var loaded = await store.Data.Messages.LoadAllAsync(cancellationToken);
        var sentToBob = loaded[0];
        var fromBob = loaded[1];

        Assert.False(await store.Data.Messages.DeleteWaitingAsync(carol.IdentityKey, sentToBob.Id, cancellationToken));
        Assert.False(await store.Data.Messages.DeleteWaitingAsync(bob.IdentityKey, fromBob.Id, cancellationToken));
        Assert.False(await store.Data.Messages.DeleteWaitingAsync(bob.IdentityKey, MessageId.Create(), cancellationToken));
        Assert.True(await store.Data.Messages.MarkReceivedAsync(bob.IdentityKey, sentToBob.Id, cancellationToken));
        Assert.False(await store.Data.Messages.DeleteWaitingAsync(bob.IdentityKey, sentToBob.Id, cancellationToken));

        Assert.Equal(3, (await store.Data.Messages.LoadAllAsync(cancellationToken)).Count);
        Assert.Empty(await store.Data.Messages.LoadSentSequencesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-20")]
    public async Task Erase_RemovesEveryRowAndTheDataKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        user.Dispose();
        bob.Dispose();
        carol.Dispose();
        await store.CloseAsync();

        await using (var database = (await MessengerDatabase.OpenAsync(store.FilePath, cancellationToken)).Database!)
        {
            await database.SaveDataKeyAsync(new WrappedDataKey(DataKeyProtection.WindowsDpapi, [1, 2, 3]), cancellationToken);
            Assert.True(await database.HoldsDataAsync(cancellationToken));

            await database.EraseAsync(cancellationToken);

            Assert.Null(await database.ReadDataKeyAsync(cancellationToken));
            Assert.False(await database.HoldsDataAsync(cancellationToken));
        }

        await using var raw = await store.RawAsync(cancellationToken);
        foreach (var table in new[] { "person", "identity", "contact", "conversation", "message", "message_delivery", "seen_message", "data_key" })
        {
            Assert.Equal(0, await Sql.ScalarAsync(raw, $"SELECT count(*) FROM {table};", cancellationToken));
        }

        Assert.Equal("ok", Assert.Single(await Sql.StringsAsync(raw, "PRAGMA integrity_check;", cancellationToken)));
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task Schema_SentSequenceShorterThanASealedValue_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        user.Dispose();
        bob.Dispose();
        carol.Dispose();
        await using var raw = await store.RawAsync(cancellationToken);

        var refused = await Assert.ThrowsAsync<SqliteException>(() =>
            Sql.ExecuteAsync(raw, "UPDATE conversation SET sent_sequence_enc = $value;", [("$value", new byte[39])], cancellationToken));

        Assert.Equal(19, refused.SqliteErrorCode);
    }

    private static async Task<byte[]> ReadSequenceAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        Assert.Single(await BlobsAsync(connection, "SELECT sent_sequence_enc FROM conversation WHERE sent_sequence_enc IS NOT NULL;", cancellationToken));

    private static async Task<List<byte[]>> BlobsAsync(SqliteConnection connection, string query, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var blobs = new List<byte[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            blobs.Add(reader.GetFieldValue<byte[]>(0));
        }

        return blobs;
    }

    private static IEnumerable<string> DatabaseFiles(string path) =>
        new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists);

    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var content = new byte[stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }
}
