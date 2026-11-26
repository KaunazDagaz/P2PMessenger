using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class GroupAcknowledgementRepositoryTests
{
    [Fact]
    [Trait("Story", "US-17")]
    public async Task Acknowledgements_AddedLoadedAndRemoved_RoundTripAcrossReopenAndDeduplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "Hi team", 4);
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
            var carolAck = GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, carol);

            Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));

            await store.ReopenAsync(Samples.DataKey(), cancellationToken);
            var loaded = Assert.Single(await store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
            Assert.Equal(carolAck, loaded);

            Assert.True(await store.Data.GroupMessages.RemoveAcknowledgementAsync(record.GroupId, carol.IdentityKey, fromBob.Id, cancellationToken));
            Assert.False(await store.Data.GroupMessages.RemoveAcknowledgementAsync(record.GroupId, carol.IdentityKey, fromBob.Id, cancellationToken));
            Assert.Empty(await store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public async Task AddAcknowledgement_UnanchoredOwnOrInvalidCases_AreRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var dave = KeySet.Create();
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var own = GroupSamples.Message(record, key, user, 1, "Own", 3, bob.IdentityKey, carol.IdentityKey);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "From Bob", 4);
            Assert.True(await store.Data.GroupMessages.AddAsync(own, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));

            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, MessageId.Create(), carol), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(GroupId.Create(), 1, fromBob.Id, carol), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 2, fromBob.Id, carol), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, own.Id, carol), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, bob), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, user), cancellationToken));
            Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, dave), cancellationToken));

            var forgedBytes = GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, bob).ToBytes();
            carol.IdentityKey.Bytes.CopyTo(forgedBytes.AsSpan(GroupId.Length + sizeof(uint) + MessageId.Length));
            Assert.True(GroupAcknowledgement.TryParse(forgedBytes, out var forged));
            await Assert.ThrowsAsync<ArgumentException>(() => store.Data.GroupMessages.AddAcknowledgementAsync(forged, cancellationToken));
            Assert.Empty(await store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteConversation_CascadesAcknowledgementsAndLeavesNoPlaintextInFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        byte[] ackPlain;
        byte[] ackSealed;
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "Hi team", 4);
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
            var carolAck = GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, carol);
            ackPlain = carolAck.ToBytes();
            Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));
            await using (var raw = await store.RawAsync(cancellationToken))
            {
                ackSealed = Assert.Single(await BlobsAsync(raw, "SELECT ack_enc FROM group_acknowledgement;", cancellationToken));
            }
        }

        foreach (var path in new[] { store.FilePath, store.FilePath + "-wal", store.FilePath + "-shm" }.Where(File.Exists))
        {
            var content = await ReadSharedAsync(path, cancellationToken);
            Assert.Equal(-1, content.AsSpan().IndexOf(ackPlain));
        }

        Assert.Equal(1, await store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [new AuthorSequenceRange(bob.IdentityKey, 1, 1)], cancellationToken));
        Assert.Empty(await store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));

        foreach (var path in new[] { store.FilePath, store.FilePath + "-wal", store.FilePath + "-shm" }.Where(File.Exists))
        {
            var content = await ReadSharedAsync(path, cancellationToken);
            Assert.Equal(-1, content.AsSpan().IndexOf(ackSealed));
        }
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    public async Task LoadAcknowledgements_EveryFlippedBitAndMovedRow_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var first = GroupSamples.Message(record, key, bob, 1, "First", 4);
            var second = GroupSamples.Message(record, key, bob, 2, "Second", 5);
            Assert.True(await store.Data.GroupMessages.AddAsync(first, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(second, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, first.Id, carol), cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(GroupAcknowledgement.Create(record.GroupId, 1, second.Id, carol), cancellationToken));
        }

        await using var raw = await store.RawAsync(cancellationToken);
        var original = Assert.Single(await BlobsAsync(raw, "SELECT ack_enc FROM group_acknowledgement ORDER BY message_id LIMIT 1;", cancellationToken));
        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            await Sql.ExecuteAsync(
                raw,
                "UPDATE group_acknowledgement SET ack_enc = $value WHERE message_id = (SELECT min(message_id) FROM group_acknowledgement);",
                [("$value", changed)],
                cancellationToken);

            await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
        }

        await Sql.ExecuteAsync(
            raw,
            "UPDATE group_acknowledgement SET ack_enc = (SELECT ack_enc FROM group_acknowledgement ORDER BY message_id DESC LIMIT 1) WHERE message_id = (SELECT min(message_id) FROM group_acknowledgement);",
            [],
            cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));

        await Sql.ExecuteAsync(
            raw,
            "UPDATE group_acknowledgement SET ack_enc = $value WHERE message_id = (SELECT min(message_id) FROM group_acknowledgement);",
            [("$value", original)],
            cancellationToken);
        Assert.Equal(2, (await store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken)).Count);
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public async Task LoadAcknowledgements_ResealedForgedSignatureAndRecipientAadBinding_AreVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        byte[] forgedPlain;
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "Signed check", 4);
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
            var genuine = GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, carol);
            Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(genuine, cancellationToken));
            forgedPlain = genuine.ToBytes();
            forgedPlain[^1] ^= 1;
        }

        using var keys = RecordKeys.Derive(Samples.DataKey());
        var locA = Locations.GroupAcknowledgement(1, 2);
        var locB = Locations.GroupAcknowledgement(1, 3);
        Assert.NotEqual(locA, locB);
        var sealedForA = keys.Seal(locA, forgedPlain);
        Assert.False(keys.TryOpen(locB, sealedForA, out _));

        await using var raw = await store.RawAsync(cancellationToken);
        long messageRow;
        long recipientRow;
        using (var select = raw.CreateCommand())
        {
            select.CommandText = "SELECT message_id, recipient_person_id FROM group_acknowledgement LIMIT 1;";
            using var reader = await select.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            messageRow = reader.GetInt64(0);
            recipientRow = reader.GetInt64(1);
        }

        var resealedForged = keys.Seal(Locations.GroupAcknowledgement(messageRow, recipientRow), forgedPlain);
        await Sql.ExecuteAsync(
            raw,
            "UPDATE group_acknowledgement SET ack_enc = $value WHERE message_id = $m AND recipient_person_id = $r;",
            [("$value", resealedForged), ("$m", messageRow), ("$r", recipientRow)],
            cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-17")]
    public async Task Schema_GroupAcknowledgementShorterThanSealedOrOnMissingMessage_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            Assert.True(await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, bob, 1, "Hi", 4), cancellationToken));
        }

        await using var raw = await store.RawAsync(cancellationToken);
        await Assert.ThrowsAsync<SqliteException>(() =>
            Sql.ExecuteAsync(raw, "INSERT INTO group_acknowledgement (message_id, recipient_person_id, ack_enc) VALUES (999, 3, $blob);", [("$blob", new byte[40])], cancellationToken));
        await Assert.ThrowsAsync<SqliteException>(() =>
            Sql.ExecuteAsync(raw, "INSERT INTO group_acknowledgement (message_id, recipient_person_id, ack_enc) VALUES ((SELECT max(id) FROM message), 3, $short);", [("$short", new byte[39])], cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-17")]
    [Trait("NFR", "NFR-09")]
    public async Task AddAcknowledgement_MismatchedEnvelopeGroupAuthorOrMessageId_IsRefusedImmediately()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
        var fromBob = GroupSamples.Message(record, key, bob, 1, "Hi", 4);
        Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
        var carolAck = GroupAcknowledgement.Create(record.GroupId, 1, fromBob.Id, carol);
        Assert.True(GroupText.TryCreate(fromBob.Id, 1, Samples.WrittenAt.AddMinutes(4), "Hi", out var groupText));

        using var keys = RecordKeys.Derive(Samples.DataKey());
        await using var raw = await store.RawAsync(cancellationToken);
        long messageRow;
        byte[] originalMessageIdEnc;
        byte[] originalEnvelopeEnc;
        using (var select = raw.CreateCommand())
        {
            select.CommandText = "SELECT id, message_id_enc, group_envelope_enc FROM message ORDER BY id DESC LIMIT 1;";
            using var reader = await select.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            messageRow = reader.GetInt64(0);
            originalMessageIdEnc = reader.GetFieldValue<byte[]>(1);
            originalEnvelopeEnc = reader.GetFieldValue<byte[]>(2);
        }

        var wrongGroupEnvelope = GroupMessage.Seal(key, GroupId.Create(), 1, bob, groupText);
        var wrongAuthorEnvelope = GroupMessage.Seal(key, record.GroupId, 1, carol, groupText);
        key.Dispose();

        await Sql.ExecuteAsync(
            raw,
            "UPDATE message SET group_envelope_enc = $value WHERE id = $id;",
            [("$value", keys.Seal(Locations.MessageGroupEnvelope(messageRow), wrongGroupEnvelope.ToBytes())), ("$id", messageRow)],
            cancellationToken);
        Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));

        await Sql.ExecuteAsync(
            raw,
            "UPDATE message SET group_envelope_enc = $value WHERE id = $id;",
            [("$value", keys.Seal(Locations.MessageGroupEnvelope(messageRow), wrongAuthorEnvelope.ToBytes())), ("$id", messageRow)],
            cancellationToken);
        Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));

        await Sql.ExecuteAsync(
            raw,
            "UPDATE message SET group_envelope_enc = $env, message_id_enc = $mid WHERE id = $id;",
            [("$env", originalEnvelopeEnc), ("$mid", keys.Seal(Locations.MessageId(messageRow), MessageId.Create().Bytes)), ("$id", messageRow)],
            cancellationToken);
        Assert.False(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));

        await Sql.ExecuteAsync(
            raw,
            "UPDATE message SET message_id_enc = $mid WHERE id = $id;",
            [("$mid", originalMessageIdEnc), ("$id", messageRow)],
            cancellationToken);
        Assert.True(await store.Data.GroupMessages.AddAcknowledgementAsync(carolAck, cancellationToken));

        await Sql.ExecuteAsync(
            raw,
            "UPDATE message SET group_envelope_enc = $value WHERE id = $id;",
            [("$value", keys.Seal(Locations.MessageGroupEnvelope(messageRow), wrongAuthorEnvelope.ToBytes())), ("$id", messageRow)],
            cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadAcknowledgementsAsync(cancellationToken));
    }

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

    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var content = new byte[stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }
}
