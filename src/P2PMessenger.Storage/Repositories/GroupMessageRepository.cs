using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class GroupMessageRepository
{
    private const string SelectMessages =
        "SELECT message.id, message.author_person_id, message.message_id_mac, message.message_id_enc, message.sender_sequence, message.text_enc, "
        + "message.written_at_enc, message.recorded_at_enc, message.group_envelope_enc, chat_group.id, chat_group.shared_id_mac, chat_group.shared_id_enc, "
        + "person.identity_key_mac, person.identity_key_enc "
        + "FROM message "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN chat_group ON chat_group.id = conversation.group_id "
        + "JOIN person ON person.id = message.author_person_id "
        + "ORDER BY message.id;";

    private const string SelectDeliveries =
        "SELECT delivery.message_id, delivery.state, person.id, person.identity_key_mac, person.identity_key_enc "
        + "FROM message_delivery AS delivery "
        + "JOIN message ON message.id = delivery.message_id "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN person ON person.id = delivery.recipient_person_id "
        + "WHERE conversation.group_id IS NOT NULL "
        + "ORDER BY delivery.message_id, person.id;";

    private const string SelectOwnPerson = "SELECT person_id FROM identity WHERE id = 1;";
    private const string SelectGroupByLookup = "SELECT id FROM chat_group WHERE shared_id_mac = $shared_id_mac;";
    private const string SelectConversation = "SELECT id FROM conversation WHERE group_id = $group_id;";

    private const string SelectMemberPerson =
        "SELECT person.id FROM person JOIN group_member ON group_member.person_id = person.id "
        + "WHERE group_member.group_id = $group_id AND person.identity_key_mac = $identity_key_mac;";

    private const string SelectTakenSequence =
        "SELECT count(*) FROM message WHERE conversation_id = $conversation_id AND author_person_id = $author_person_id AND sender_sequence = $sender_sequence;";

    private const string NextMessageId = "SELECT coalesce(max(id), 0) + 1 FROM message;";

    private const string InsertMessage =
        "INSERT INTO message (id, conversation_id, author_person_id, message_id_mac, message_id_enc, sender_sequence, text_enc, written_at_enc, recorded_at_enc, group_envelope_enc) "
        + "VALUES ($id, $conversation_id, $author_person_id, $message_id_mac, $message_id_enc, $sender_sequence, $text_enc, $written_at_enc, $recorded_at_enc, $group_envelope_enc);";

    private const string InsertDelivery =
        "INSERT INTO message_delivery (message_id, recipient_person_id, state) VALUES ($message_id, $recipient_person_id, $state);";

    private const string InsertSeen =
        "INSERT INTO seen_message (message_id_mac) VALUES ($message_id_mac) ON CONFLICT (message_id_mac) DO NOTHING;";

    private const string MarkDeliveryReceived =
        "UPDATE message_delivery SET state = 2 "
        + "WHERE state = 1 "
        + "AND message_id = ("
        + "SELECT message.id FROM message JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN chat_group ON chat_group.id = conversation.group_id "
        + "WHERE message.message_id_mac = $message_id_mac AND chat_group.shared_id_mac = $shared_id_mac) "
        + "AND recipient_person_id = (SELECT id FROM person WHERE identity_key_mac = $identity_key_mac);";

    private const string SelectDeletedRanges =
        "SELECT conversation.id, conversation.deleted_ranges_enc, chat_group.id, chat_group.shared_id_mac, chat_group.shared_id_enc "
        + "FROM conversation "
        + "JOIN chat_group ON chat_group.id = conversation.group_id "
        + "WHERE conversation.deleted_ranges_enc IS NOT NULL "
        + "ORDER BY conversation.id;";

    private const string SelectConversationByLookup =
        "SELECT conversation.id FROM conversation JOIN chat_group ON chat_group.id = conversation.group_id WHERE chat_group.shared_id_mac = $shared_id_mac;";

    private const string DeleteConversationMessages = "DELETE FROM message WHERE conversation_id = $conversation_id;";

    private const string UpdateDeletedRanges = "UPDATE conversation SET deleted_ranges_enc = $deleted_ranges_enc WHERE id = $conversation_id;";

    private const string SelectWaitingOwnMessage =
        "SELECT message.id FROM message "
        + "WHERE message.message_id_mac = $message_id_mac "
        + "AND message.conversation_id = $conversation_id "
        + "AND message.author_person_id = $author_person_id "
        + "AND EXISTS (SELECT 1 FROM message_delivery WHERE message_delivery.message_id = message.id) "
        + "AND NOT EXISTS (SELECT 1 FROM message_delivery WHERE message_delivery.message_id = message.id AND message_delivery.state = 2);";

    private const string DeleteMessage = "DELETE FROM message WHERE id = $id;";

    private const string SelectAcknowledgements =
        "SELECT ack.message_id, ack.recipient_person_id, ack.ack_enc, message.author_person_id, "
        + "message.message_id_mac, message.message_id_enc, message.group_envelope_enc, "
        + "chat_group.id, chat_group.shared_id_mac, chat_group.shared_id_enc, "
        + "recipient.identity_key_mac, recipient.identity_key_enc, "
        + "author.identity_key_mac, author.identity_key_enc "
        + "FROM group_acknowledgement AS ack "
        + "JOIN message ON message.id = ack.message_id "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "LEFT JOIN chat_group ON chat_group.id = conversation.group_id "
        + "JOIN person AS recipient ON recipient.id = ack.recipient_person_id "
        + "JOIN person AS author ON author.id = message.author_person_id "
        + "ORDER BY ack.message_id, ack.recipient_person_id;";

    private const string SelectMessageForAcknowledgement =
        "SELECT message.id, message.author_person_id, message.message_id_mac, message.message_id_enc, message.group_envelope_enc, "
        + "chat_group.id, chat_group.shared_id_mac, chat_group.shared_id_enc, "
        + "author.identity_key_mac, author.identity_key_enc "
        + "FROM message "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN chat_group ON chat_group.id = conversation.group_id "
        + "JOIN person AS author ON author.id = message.author_person_id "
        + "WHERE conversation.id = $conversation_id AND message.message_id_mac = $message_id_mac;";

    private const string InsertAcknowledgement =
        "INSERT INTO group_acknowledgement (message_id, recipient_person_id, ack_enc) "
        + "VALUES ($message_id, $recipient_person_id, $ack_enc) "
        + "ON CONFLICT (message_id, recipient_person_id) DO NOTHING;";

    private const string DeleteAcknowledgement =
        "DELETE FROM group_acknowledgement "
        + "WHERE message_id = ("
        + "SELECT message.id FROM message JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN chat_group ON chat_group.id = conversation.group_id "
        + "WHERE message.message_id_mac = $message_id_mac AND chat_group.shared_id_mac = $shared_id_mac) "
        + "AND recipient_person_id = (SELECT id FROM person WHERE identity_key_mac = $identity_key_mac);";

    private const int RangeLength = Ed25519PublicKey.Length + (2 * sizeof(long));

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal GroupMessageRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<IReadOnlyList<StoredGroupMessage>> LoadAllAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAllAsync, cancellationToken);

    public Task<bool> AddAsync(StoredGroupMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.GroupId);
        ArgumentNullException.ThrowIfNull(message.AuthorKey);
        ArgumentNullException.ThrowIfNull(message.Id);
        ArgumentNullException.ThrowIfNull(message.Text);
        ArgumentNullException.ThrowIfNull(message.Envelope);
        ArgumentNullException.ThrowIfNull(message.Deliveries);
        ArgumentOutOfRangeException.ThrowIfLessThan(message.Sequence, 1);
        if (!message.Envelope.GroupId.Equals(message.GroupId) || !message.Envelope.AuthorKey.Equals(message.AuthorKey))
        {
            throw new ArgumentException("The envelope belongs to another group or author.", nameof(message));
        }

        if (message.Deliveries.Values.Any(static state => state is not (StoredDeliveryState.Waiting or StoredDeliveryState.Received)))
        {
            throw new ArgumentException("A delivery is waiting or received.", nameof(message));
        }

        return gate.WriteAsync((connection, transaction, token) => AddAsync(connection, transaction, message, token), cancellationToken);
    }

    public Task<bool> MarkReceivedAsync(GroupId groupId, Ed25519PublicKey recipientKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(recipientKey);
        ArgumentNullException.ThrowIfNull(id);

        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        byte[] recipientLookup = keys.Lookup(LookupDomain.IdentityKey, recipientKey.Bytes);
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, id.Bytes);
        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                int changed = await Sql.ExecuteAsync(
                    connection,
                    transaction,
                    MarkDeliveryReceived,
                    token,
                    ("$message_id_mac", idLookup),
                    ("$shared_id_mac", groupLookup),
                    ("$identity_key_mac", recipientLookup)).ConfigureAwait(false);
                return changed == 1;
            },
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>> LoadDeletedRangesAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadDeletedRangesAsync, cancellationToken);

    public async Task<int> DeleteConversationAsync(GroupId groupId, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        byte[] ranges = EncodeRanges(deleted);
        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        int removed = await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                if (await ConversationOfAsync(connection, transaction, groupLookup, token).ConfigureAwait(false) is not { } conversationId)
                {
                    return 0;
                }

                int count = await Sql.ExecuteAsync(connection, transaction, DeleteConversationMessages, token, ("$conversation_id", conversationId)).ConfigureAwait(false);
                await KeepRangesAsync(connection, transaction, conversationId, ranges, token).ConfigureAwait(false);
                return count;
            },
            cancellationToken).ConfigureAwait(false);

        if (removed > 0)
        {
            await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(id);

        byte[] ranges = EncodeRanges(deleted);
        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, id.Bytes);
        bool removed = await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                if (await ConversationOfAsync(connection, transaction, groupLookup, token).ConfigureAwait(false) is not { } conversationId
                    || await Sql.LongAsync(connection, transaction, SelectOwnPerson, token).ConfigureAwait(false) is not { } ownPersonId)
                {
                    return false;
                }

                long? rowId = await Sql.LongAsync(
                    connection,
                    transaction,
                    SelectWaitingOwnMessage,
                    token,
                    ("$message_id_mac", idLookup),
                    ("$conversation_id", conversationId),
                    ("$author_person_id", ownPersonId)).ConfigureAwait(false);
                if (rowId is not { } row)
                {
                    return false;
                }

                await Sql.ExecuteAsync(connection, transaction, DeleteMessage, token, ("$id", row)).ConfigureAwait(false);
                await KeepRangesAsync(connection, transaction, conversationId, ranges, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (removed)
        {
            await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    public Task<IReadOnlyList<GroupAcknowledgement>> LoadAcknowledgementsAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAcknowledgementsAsync, cancellationToken);

    public Task<bool> AddAcknowledgementAsync(GroupAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (!acknowledgement.HasValidSignature())
        {
            throw new ArgumentException("The group acknowledgement does not carry a valid signature.", nameof(acknowledgement));
        }

        return gate.WriteAsync((connection, transaction, token) => AddAcknowledgementAsync(connection, transaction, acknowledgement, token), cancellationToken);
    }

    public Task<bool> RemoveAcknowledgementAsync(GroupId groupId, Ed25519PublicKey recipientKey, MessageId messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(recipientKey);
        ArgumentNullException.ThrowIfNull(messageId);

        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, groupId.Bytes);
        byte[] recipientLookup = keys.Lookup(LookupDomain.IdentityKey, recipientKey.Bytes);
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, messageId.Bytes);
        return gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                int changed = await Sql.ExecuteAsync(
                    connection,
                    transaction,
                    DeleteAcknowledgement,
                    token,
                    ("$message_id_mac", idLookup),
                    ("$shared_id_mac", groupLookup),
                    ("$identity_key_mac", recipientLookup)).ConfigureAwait(false);
                return changed == 1;
            },
            cancellationToken);
    }

    private static byte[] EncodeRanges(IReadOnlyList<AuthorSequenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        byte[] bytes = new byte[sizeof(int) + (ranges.Count * RangeLength)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, ranges.Count);
        AuthorSequenceRange? previous = null;
        int offset = sizeof(int);
        foreach (AuthorSequenceRange range in ranges)
        {
            ArgumentNullException.ThrowIfNull(range);
            ArgumentNullException.ThrowIfNull(range.AuthorKey);
            if (!Follows(previous, range))
            {
                throw new ArgumentException("Deleted ranges are sorted by author and first sequence, start at 1 and neither overlap nor touch.", nameof(ranges));
            }

            range.AuthorKey.Bytes.CopyTo(bytes.AsSpan(offset));
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset + Ed25519PublicKey.Length), range.First);
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset + Ed25519PublicKey.Length + sizeof(long)), range.Last);
            offset += RangeLength;
            previous = range;
        }

        return bytes;
    }

    private static List<AuthorSequenceRange> DecodeRanges(RecordLocation location, byte[] bytes)
    {
        if (bytes.Length < sizeof(int))
        {
            throw StoredValues.Damaged(location);
        }

        int count = BinaryPrimitives.ReadInt32BigEndian(bytes);
        if (count < 1 || (long)count * RangeLength != bytes.Length - sizeof(int))
        {
            throw StoredValues.Damaged(location);
        }

        List<AuthorSequenceRange> ranges = new(count);
        AuthorSequenceRange? previous = null;
        for (int offset = sizeof(int); offset < bytes.Length; offset += RangeLength)
        {
            if (!Ed25519PublicKey.TryCreate(bytes.AsSpan(offset, Ed25519PublicKey.Length), out Ed25519PublicKey? author)
                || !SequenceRange.TryCreate(
                    BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset + Ed25519PublicKey.Length)),
                    BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset + Ed25519PublicKey.Length + sizeof(long))),
                    out SequenceRange sequenceRange))
            {
                throw StoredValues.Damaged(location);
            }

            AuthorSequenceRange range = new(author, sequenceRange);
            if (!Follows(previous, range))
            {
                throw StoredValues.Damaged(location);
            }

            ranges.Add(range);
            previous = range;
        }

        return ranges;
    }

    private static bool Follows(AuthorSequenceRange? previous, AuthorSequenceRange range)
    {
        if (range.First < 1 || range.Last < range.First)
        {
            return false;
        }

        if (previous is null)
        {
            return true;
        }

        int order = previous.AuthorKey.Bytes.SequenceCompareTo(range.AuthorKey.Bytes);
        return order < 0 || (order == 0 && previous.Last < long.MaxValue && range.First > previous.Last + 1);
    }

    private static Task<long?> ConversationOfAsync(SqliteConnection connection, SqliteTransaction transaction, byte[] groupLookup, CancellationToken cancellationToken) =>
        Sql.LongAsync(
            connection,
            transaction,
            SelectConversationByLookup,
            cancellationToken,
            ("$shared_id_mac", groupLookup));

    private async Task KeepRangesAsync(SqliteConnection connection, SqliteTransaction transaction, long conversationId, byte[] ranges, CancellationToken cancellationToken)
    {
        if (BinaryPrimitives.ReadInt32BigEndian(ranges) == 0)
        {
            return;
        }

        await Sql.ExecuteAsync(
            connection,
            transaction,
            UpdateDeletedRanges,
            cancellationToken,
            ("$deleted_ranges_enc", keys.Seal(Locations.ConversationDeletedRanges(conversationId), ranges)),
            ("$conversation_id", conversationId)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>> LoadDeletedRangesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<GroupId, IReadOnlyList<AuthorSequenceRange>> deleted = [];
        using SqliteCommand command = Sql.Command(connection, null, SelectDeletedRanges);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long conversationId = reader.GetInt64(0);
            long groupRow = reader.GetInt64(2);
            RecordLocation groupLocation = Locations.GroupSharedId(groupRow);
            byte[] groupBytes = StoredValues.Open(keys, groupLocation, reader.GetFieldValue<byte[]>(4));
            if (!GroupId.TryCreate(groupBytes, out GroupId? groupId) || !keys.MatchesLookup(LookupDomain.GroupId, groupBytes, reader.GetFieldValue<byte[]>(3)))
            {
                throw StoredValues.Damaged(groupLocation);
            }

            RecordLocation location = Locations.ConversationDeletedRanges(conversationId);
            List<AuthorSequenceRange> ranges = DecodeRanges(location, StoredValues.Open(keys, location, reader.GetFieldValue<byte[]>(1)));
            if (!deleted.TryAdd(groupId, ranges))
            {
                throw StoredValues.Inconsistent("two conversations belong to one group");
            }
        }

        return deleted;
    }

    private async Task<IReadOnlyList<StoredGroupMessage>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long? ownPersonId = await Sql.LongAsync(connection, null, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        Dictionary<long, Dictionary<Ed25519PublicKey, StoredDeliveryState>> deliveries = [];
        using (SqliteCommand command = Sql.Command(connection, null, SelectDeliveries))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long messageRow = reader.GetInt64(0);
                StoredDeliveryState state = reader.GetInt64(1) switch
                {
                    1 => StoredDeliveryState.Waiting,
                    2 => StoredDeliveryState.Received,
                    _ => throw StoredValues.Inconsistent("a group delivery has an unknown state"),
                };
                Ed25519PublicKey recipient = StoredValues.IdentityKey(keys, reader.GetInt64(2), reader.GetFieldValue<byte[]>(3), reader.GetFieldValue<byte[]>(4));
                if (!deliveries.TryGetValue(messageRow, out Dictionary<Ed25519PublicKey, StoredDeliveryState>? states))
                {
                    states = [];
                    deliveries.Add(messageRow, states);
                }

                states.Add(recipient, state);
            }
        }

        List<StoredGroupMessage> messages = [];
        using (SqliteCommand command = Sql.Command(connection, null, SelectMessages))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(Read(reader, ownPersonId, deliveries));
            }
        }

        return messages;
    }

    private StoredGroupMessage Read(SqliteDataReader reader, long? ownPersonId, Dictionary<long, Dictionary<Ed25519PublicKey, StoredDeliveryState>> deliveries)
    {
        long rowId = reader.GetInt64(0);
        long authorId = reader.GetInt64(1);
        long groupRow = reader.GetInt64(9);

        RecordLocation groupLocation = Locations.GroupSharedId(groupRow);
        byte[] groupBytes = StoredValues.Open(keys, groupLocation, reader.GetFieldValue<byte[]>(11));
        if (!GroupId.TryCreate(groupBytes, out GroupId? groupId) || !keys.MatchesLookup(LookupDomain.GroupId, groupBytes, reader.GetFieldValue<byte[]>(10)))
        {
            throw StoredValues.Damaged(groupLocation);
        }

        Ed25519PublicKey authorKey = StoredValues.IdentityKey(keys, authorId, reader.GetFieldValue<byte[]>(12), reader.GetFieldValue<byte[]>(13));

        RecordLocation idLocation = Locations.MessageId(rowId);
        byte[] idBytes = StoredValues.Open(keys, idLocation, reader.GetFieldValue<byte[]>(3));
        if (!MessageId.TryCreate(idBytes, out MessageId? id) || !keys.MatchesLookup(LookupDomain.MessageId, idBytes, reader.GetFieldValue<byte[]>(2)))
        {
            throw StoredValues.Damaged(idLocation);
        }

        RecordLocation textLocation = Locations.MessageText(rowId);
        string text = StoredValues.Text(textLocation, StoredValues.Open(keys, textLocation, reader.GetFieldValue<byte[]>(5)));

        RecordLocation writtenLocation = Locations.MessageWrittenAt(rowId);
        DateTimeOffset writtenAt = StoredValues.Time(writtenLocation, StoredValues.Open(keys, writtenLocation, reader.GetFieldValue<byte[]>(6)));

        RecordLocation recordedLocation = Locations.MessageRecordedAt(rowId);
        DateTimeOffset recordedAt = StoredValues.Time(recordedLocation, StoredValues.Open(keys, recordedLocation, reader.GetFieldValue<byte[]>(7)));

        RecordLocation envelopeLocation = Locations.MessageGroupEnvelope(rowId);
        if (reader.IsDBNull(8)
            || !GroupMessage.TryParse(StoredValues.Open(keys, envelopeLocation, reader.GetFieldValue<byte[]>(8)), out GroupMessage? envelope)
            || !envelope.GroupId.Equals(groupId)
            || !envelope.AuthorKey.Equals(authorKey))
        {
            throw StoredValues.Damaged(envelopeLocation);
        }

        bool own = ownPersonId is { } ownId && authorId == ownId;
        Dictionary<Ed25519PublicKey, StoredDeliveryState>? states = deliveries.GetValueOrDefault(rowId);
        if (own != (states is { Count: > 0 }))
        {
            throw StoredValues.Inconsistent(own ? "the user's group message has no delivery state" : "a received group message has a delivery state");
        }

        return new StoredGroupMessage(groupId, authorKey, id, reader.GetInt64(4), writtenAt, text, recordedAt, envelope)
        {
            Deliveries = states ?? [],
        };
    }

    private async Task<bool> AddAsync(SqliteConnection connection, SqliteTransaction transaction, StoredGroupMessage message, CancellationToken cancellationToken)
    {
        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, message.GroupId.Bytes);
        long groupRow = await Sql.LongAsync(connection, transaction, SelectGroupByLookup, cancellationToken, ("$shared_id_mac", groupLookup)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The message's group is not stored.");
        long conversationId = await Sql.LongAsync(connection, transaction, SelectConversation, cancellationToken, ("$group_id", groupRow)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The group has no conversation.");
        long authorId = await MemberAsync(connection, transaction, groupRow, message.AuthorKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The message's author is not a member of the group.");

        long taken = await Sql.LongAsync(
            connection,
            transaction,
            SelectTakenSequence,
            cancellationToken,
            ("$conversation_id", conversationId),
            ("$author_person_id", authorId),
            ("$sender_sequence", message.Sequence)).ConfigureAwait(false) ?? 0;
        if (taken > 0)
        {
            return false;
        }

        long rowId = await Sql.LongAsync(connection, transaction, NextMessageId, cancellationToken).ConfigureAwait(false) ?? 1;
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, message.Id.Bytes);
        await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertMessage,
            cancellationToken,
            ("$id", rowId),
            ("$conversation_id", conversationId),
            ("$author_person_id", authorId),
            ("$message_id_mac", idLookup),
            ("$message_id_enc", keys.Seal(Locations.MessageId(rowId), message.Id.Bytes)),
            ("$sender_sequence", message.Sequence),
            ("$text_enc", keys.Seal(Locations.MessageText(rowId), StoredValues.Utf8(message.Text))),
            ("$written_at_enc", keys.Seal(Locations.MessageWrittenAt(rowId), StoredValues.Time(message.WrittenAt))),
            ("$recorded_at_enc", keys.Seal(Locations.MessageRecordedAt(rowId), StoredValues.Time(message.RecordedAt))),
            ("$group_envelope_enc", keys.Seal(Locations.MessageGroupEnvelope(rowId), message.Envelope.ToBytes()))).ConfigureAwait(false);

        foreach ((Ed25519PublicKey recipient, StoredDeliveryState state) in message.Deliveries)
        {
            long recipientId = await MemberAsync(connection, transaction, groupRow, recipient, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A recipient is not a member of the group.");
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertDelivery,
                cancellationToken,
                ("$message_id", rowId),
                ("$recipient_person_id", recipientId),
                ("$state", (long)state)).ConfigureAwait(false);
        }

        await Sql.ExecuteAsync(connection, transaction, InsertSeen, cancellationToken, ("$message_id_mac", idLookup)).ConfigureAwait(false);
        return true;
    }

    private async Task<IReadOnlyList<GroupAcknowledgement>> LoadAcknowledgementsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long? ownPersonId = await Sql.LongAsync(connection, null, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        List<GroupAcknowledgement> loaded = [];
        using SqliteCommand command = Sql.Command(connection, null, SelectAcknowledgements);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long messageRow = reader.GetInt64(0);
            long recipientId = reader.GetInt64(1);
            long authorId = reader.GetInt64(3);
            if (reader.IsDBNull(7))
            {
                throw StoredValues.Inconsistent("a group acknowledgement belongs to a one-to-one message");
            }

            if (authorId == recipientId)
            {
                throw StoredValues.Inconsistent("a group acknowledgement names the message author");
            }

            if (ownPersonId is { } ownId && (authorId == ownId || recipientId == ownId))
            {
                throw StoredValues.Inconsistent("a relayed group acknowledgement belongs to the user's own message or recipient");
            }

            long groupRow = reader.GetInt64(7);
            RecordLocation groupLocation = Locations.GroupSharedId(groupRow);
            byte[] groupBytes = StoredValues.Open(keys, groupLocation, reader.GetFieldValue<byte[]>(9));
            if (!GroupId.TryCreate(groupBytes, out GroupId? groupId) || !keys.MatchesLookup(LookupDomain.GroupId, groupBytes, reader.GetFieldValue<byte[]>(8)))
            {
                throw StoredValues.Damaged(groupLocation);
            }

            RecordLocation idLocation = Locations.MessageId(messageRow);
            byte[] idBytes = StoredValues.Open(keys, idLocation, reader.GetFieldValue<byte[]>(5));
            if (!MessageId.TryCreate(idBytes, out MessageId? messageId) || !keys.MatchesLookup(LookupDomain.MessageId, idBytes, reader.GetFieldValue<byte[]>(4)))
            {
                throw StoredValues.Damaged(idLocation);
            }

            Ed25519PublicKey authorKey = StoredValues.IdentityKey(keys, authorId, reader.GetFieldValue<byte[]>(12), reader.GetFieldValue<byte[]>(13));

            RecordLocation envelopeLocation = Locations.MessageGroupEnvelope(messageRow);
            if (reader.IsDBNull(6)
                || !GroupMessage.TryParse(StoredValues.Open(keys, envelopeLocation, reader.GetFieldValue<byte[]>(6)), out GroupMessage? envelope)
                || !envelope.GroupId.Equals(groupId)
                || !envelope.AuthorKey.Equals(authorKey))
            {
                throw StoredValues.Damaged(envelopeLocation);
            }

            Ed25519PublicKey recipientKey = StoredValues.IdentityKey(keys, recipientId, reader.GetFieldValue<byte[]>(10), reader.GetFieldValue<byte[]>(11));
            RecordLocation ackLocation = Locations.GroupAcknowledgement(messageRow, recipientId);
            byte[] ackBytes = StoredValues.Open(keys, ackLocation, reader.GetFieldValue<byte[]>(2));
            if (!GroupAcknowledgement.TryParse(ackBytes, out GroupAcknowledgement? ack)
                || !ack.GroupId.Equals(groupId)
                || ack.Epoch != envelope.Epoch
                || !ack.MessageId.Equals(messageId)
                || !ack.RecipientKey.Equals(recipientKey)
                || !ack.HasValidSignature())
            {
                throw StoredValues.Damaged(ackLocation);
            }

            loaded.Add(ack);
        }

        return loaded;
    }

    private async Task<bool> AddAcknowledgementAsync(SqliteConnection connection, SqliteTransaction transaction, GroupAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        byte[] groupLookup = keys.Lookup(LookupDomain.GroupId, acknowledgement.GroupId.Bytes);
        if (await Sql.LongAsync(connection, transaction, SelectGroupByLookup, cancellationToken, ("$shared_id_mac", groupLookup)).ConfigureAwait(false) is not { } groupRow
            || await Sql.LongAsync(connection, transaction, SelectConversation, cancellationToken, ("$group_id", groupRow)).ConfigureAwait(false) is not { } conversationId)
        {
            return false;
        }

        long? ownPersonId = await Sql.LongAsync(connection, transaction, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, acknowledgement.MessageId.Bytes);
        long messageRow;
        long authorId;
        byte[] messageIdMac;
        byte[] messageIdEnc;
        byte[]? envelopeBlob;
        long storedGroupRow;
        byte[] groupIdMac;
        byte[] groupIdEnc;
        byte[] authorKeyMac;
        byte[] authorKeyEnc;
        using (SqliteCommand command = Sql.Command(connection, transaction, SelectMessageForAcknowledgement, ("$conversation_id", conversationId), ("$message_id_mac", idLookup)))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            messageRow = reader.GetInt64(0);
            authorId = reader.GetInt64(1);
            messageIdMac = reader.GetFieldValue<byte[]>(2);
            messageIdEnc = reader.GetFieldValue<byte[]>(3);
            envelopeBlob = reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4);
            storedGroupRow = reader.GetInt64(5);
            groupIdMac = reader.GetFieldValue<byte[]>(6);
            groupIdEnc = reader.GetFieldValue<byte[]>(7);
            authorKeyMac = reader.GetFieldValue<byte[]>(8);
            authorKeyEnc = reader.GetFieldValue<byte[]>(9);
        }

        byte[] storedGroupBytes = StoredValues.Open(keys, Locations.GroupSharedId(storedGroupRow), groupIdEnc);
        byte[] storedMessageIdBytes = StoredValues.Open(keys, Locations.MessageId(messageRow), messageIdEnc);
        byte[] storedAuthorKeyBytes = StoredValues.Open(keys, Locations.PersonIdentityKey(authorId), authorKeyEnc);
        if (storedGroupRow != groupRow
            || !GroupId.TryCreate(storedGroupBytes, out GroupId? storedGroupId)
            || !keys.MatchesLookup(LookupDomain.GroupId, storedGroupBytes, groupIdMac)
            || !storedGroupId.Equals(acknowledgement.GroupId)
            || !MessageId.TryCreate(storedMessageIdBytes, out MessageId? storedMessageId)
            || !keys.MatchesLookup(LookupDomain.MessageId, storedMessageIdBytes, messageIdMac)
            || !storedMessageId.Equals(acknowledgement.MessageId)
            || !Ed25519PublicKey.TryCreate(storedAuthorKeyBytes, out Ed25519PublicKey? authorKey)
            || !keys.MatchesLookup(LookupDomain.IdentityKey, storedAuthorKeyBytes, authorKeyMac)
            || envelopeBlob is null
            || !GroupMessage.TryParse(StoredValues.Open(keys, Locations.MessageGroupEnvelope(messageRow), envelopeBlob), out GroupMessage? envelope)
            || !envelope.GroupId.Equals(acknowledgement.GroupId)
            || envelope.Epoch != acknowledgement.Epoch
            || !envelope.AuthorKey.Equals(authorKey))
        {
            return false;
        }

        if (ownPersonId is { } ownId && authorId == ownId)
        {
            return false;
        }

        if (await MemberAsync(connection, transaction, groupRow, acknowledgement.RecipientKey, cancellationToken).ConfigureAwait(false) is not { } recipientId
            || recipientId == authorId
            || (ownPersonId is { } ownRecipient && recipientId == ownRecipient))
        {
            return false;
        }

        int inserted = await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertAcknowledgement,
            cancellationToken,
            ("$message_id", messageRow),
            ("$recipient_person_id", recipientId),
            ("$ack_enc", keys.Seal(Locations.GroupAcknowledgement(messageRow, recipientId), acknowledgement.ToBytes()))).ConfigureAwait(false);
        return inserted == 1;
    }

    private Task<long?> MemberAsync(SqliteConnection connection, SqliteTransaction transaction, long groupRow, Ed25519PublicKey identityKey, CancellationToken cancellationToken) =>
        Sql.LongAsync(
            connection,
            transaction,
            SelectMemberPerson,
            cancellationToken,
            ("$group_id", groupRow),
            ("$identity_key_mac", keys.Lookup(LookupDomain.IdentityKey, identityKey.Bytes)));
}
