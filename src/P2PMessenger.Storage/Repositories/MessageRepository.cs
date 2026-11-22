using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class MessageRepository
{
    private const string SelectMessages =
        "SELECT message.id, message.author_person_id, message.message_id_mac, message.message_id_enc, message.sender_sequence, message.text_enc, "
        + "message.written_at_enc, message.recorded_at_enc, conversation.contact_person_id, person.identity_key_mac, person.identity_key_enc, "
        + "(SELECT delivery.state FROM message_delivery AS delivery WHERE delivery.message_id = message.id AND delivery.recipient_person_id = conversation.contact_person_id), "
        + "(SELECT count(*) FROM message_delivery AS delivery WHERE delivery.message_id = message.id) "
        + "FROM message "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN person ON person.id = conversation.contact_person_id "
        + "ORDER BY message.id;";

    private const string SelectOwnPerson = "SELECT person_id FROM identity WHERE id = 1;";

    private const string SelectContactPerson =
        "SELECT person.id FROM person JOIN contact ON contact.person_id = person.id WHERE person.identity_key_mac = $identity_key_mac;";

    private const string InsertConversation =
        "INSERT INTO conversation (contact_person_id) VALUES ($contact_person_id) ON CONFLICT (contact_person_id) DO NOTHING;";

    private const string SelectConversation = "SELECT id FROM conversation WHERE contact_person_id = $contact_person_id;";
    private const string NextMessageId = "SELECT coalesce(max(id), 0) + 1 FROM message;";

    private const string InsertMessage =
        "INSERT INTO message (id, conversation_id, author_person_id, message_id_mac, message_id_enc, sender_sequence, text_enc, written_at_enc, recorded_at_enc) "
        + "VALUES ($id, $conversation_id, $author_person_id, $message_id_mac, $message_id_enc, $sender_sequence, $text_enc, $written_at_enc, $recorded_at_enc);";

    private const string InsertDelivery =
        "INSERT INTO message_delivery (message_id, recipient_person_id, state) VALUES ($message_id, $recipient_person_id, $state);";

    private const string InsertSeen =
        "INSERT INTO seen_message (message_id_mac) VALUES ($message_id_mac) ON CONFLICT (message_id_mac) DO NOTHING;";

    private const string MarkDeliveryReceived =
        "UPDATE message_delivery SET state = 2 "
        + "WHERE state = 1 "
        + "AND message_id = (SELECT message.id FROM message WHERE message.message_id_mac = $message_id_mac) "
        + "AND recipient_person_id = ("
        + "SELECT conversation.contact_person_id FROM message "
        + "JOIN conversation ON conversation.id = message.conversation_id "
        + "JOIN person ON person.id = conversation.contact_person_id "
        + "WHERE message.message_id_mac = $message_id_mac AND person.identity_key_mac = $identity_key_mac);";

    private const string SelectSentSequences =
        "SELECT conversation.id, conversation.sent_sequence_enc, conversation.contact_person_id, person.identity_key_mac, person.identity_key_enc "
        + "FROM conversation "
        + "JOIN person ON person.id = conversation.contact_person_id "
        + "WHERE conversation.sent_sequence_enc IS NOT NULL "
        + "ORDER BY conversation.id;";

    private const string SelectSentSequence = "SELECT sent_sequence_enc FROM conversation WHERE id = $conversation_id;";

    private const string SelectHighestOwnSequence =
        "SELECT max(sender_sequence) FROM message WHERE conversation_id = $conversation_id AND author_person_id = $author_person_id;";

    private const string UpdateSentSequence = "UPDATE conversation SET sent_sequence_enc = $sent_sequence_enc WHERE id = $conversation_id;";

    private const string DeleteConversationMessages = "DELETE FROM message WHERE conversation_id = $conversation_id;";

    private const string SelectWaitingOwnMessage =
        "SELECT message.id FROM message "
        + "JOIN message_delivery AS delivery ON delivery.message_id = message.id AND delivery.recipient_person_id = $contact_person_id "
        + "WHERE message.message_id_mac = $message_id_mac "
        + "AND message.conversation_id = $conversation_id "
        + "AND message.author_person_id = $author_person_id "
        + "AND delivery.state = 1;";

    private const string DeleteMessage = "DELETE FROM message WHERE id = $id;";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal MessageRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<IReadOnlyList<StoredMessage>> LoadAllAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAllAsync, cancellationToken);

    public Task AddAsync(StoredMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.ContactKey);
        ArgumentNullException.ThrowIfNull(message.Id);
        ArgumentNullException.ThrowIfNull(message.Text);
        ArgumentOutOfRangeException.ThrowIfLessThan(message.Sequence, 1);
        if (!Enum.IsDefined(message.Direction))
        {
            throw new ArgumentException("The message has an unknown direction.", nameof(message));
        }

        bool deliveryFits = message.Direction == StoredMessageDirection.Outgoing
            ? message.Delivery is StoredDeliveryState.Waiting or StoredDeliveryState.Received
            : message.Delivery == StoredDeliveryState.None;
        if (!deliveryFits)
        {
            throw new ArgumentException("An outgoing message is waiting or received, and an incoming one has no delivery state.", nameof(message));
        }

        return gate.WriteAsync((connection, transaction, token) => AddAsync(connection, transaction, message, token), cancellationToken);
    }

    public Task<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        byte[] contactLookup = keys.Lookup(LookupDomain.IdentityKey, contactKey.Bytes);
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
                    ("$identity_key_mac", contactLookup)).ConfigureAwait(false);
                return changed == 1;
            },
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<Ed25519PublicKey, long>> LoadSentSequencesAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadSentSequencesAsync, cancellationToken);

    public async Task<int> DeleteConversationAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        byte[] contactLookup = keys.Lookup(LookupDomain.IdentityKey, contactKey.Bytes);
        int deleted = await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                if (await FindChatAsync(connection, transaction, contactLookup, token).ConfigureAwait(false) is not { } chat)
                {
                    return 0;
                }

                await KeepSentSequenceAsync(connection, transaction, chat, token).ConfigureAwait(false);
                return await Sql.ExecuteAsync(connection, transaction, DeleteConversationMessages, token, ("$conversation_id", chat.ConversationId)).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        byte[] contactLookup = keys.Lookup(LookupDomain.IdentityKey, contactKey.Bytes);
        byte[] idLookup = keys.Lookup(LookupDomain.MessageId, id.Bytes);
        bool deleted = await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                if (await FindChatAsync(connection, transaction, contactLookup, token).ConfigureAwait(false) is not { } chat)
                {
                    return false;
                }

                long? rowId = await Sql.LongAsync(
                    connection,
                    transaction,
                    SelectWaitingOwnMessage,
                    token,
                    ("$contact_person_id", chat.ContactPersonId),
                    ("$message_id_mac", idLookup),
                    ("$conversation_id", chat.ConversationId),
                    ("$author_person_id", chat.OwnPersonId)).ConfigureAwait(false);
                if (rowId is not { } row)
                {
                    return false;
                }

                await KeepSentSequenceAsync(connection, transaction, chat, token).ConfigureAwait(false);
                return await Sql.ExecuteAsync(connection, transaction, DeleteMessage, token, ("$id", row)).ConfigureAwait(false) == 1;
            },
            cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static StoredDeliveryState DeliveryOf(StoredMessageDirection direction, long? state, long deliveries)
    {
        if (direction == StoredMessageDirection.Incoming)
        {
            return deliveries == 0
                ? StoredDeliveryState.None
                : throw StoredValues.Inconsistent("a received message has a delivery state");
        }

        return (deliveries, state) switch
        {
            (1, (long)StoredDeliveryState.Waiting) => StoredDeliveryState.Waiting,
            (1, (long)StoredDeliveryState.Received) => StoredDeliveryState.Received,
            _ => throw StoredValues.Inconsistent("a sent message does not have exactly one delivery state for its contact"),
        };
    }

    private async Task<IReadOnlyList<StoredMessage>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long? ownPersonId = await Sql.LongAsync(connection, null, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        Dictionary<long, Ed25519PublicKey> contactKeys = [];
        List<StoredMessage> messages = [];

        using SqliteCommand command = Sql.Command(connection, null, SelectMessages);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long rowId = reader.GetInt64(0);
            long authorId = reader.GetInt64(1);
            long contactPersonId = reader.GetInt64(8);

            if (!contactKeys.TryGetValue(contactPersonId, out Ed25519PublicKey? contactKey))
            {
                contactKey = StoredValues.IdentityKey(keys, contactPersonId, reader.GetFieldValue<byte[]>(9), reader.GetFieldValue<byte[]>(10));
                contactKeys.Add(contactPersonId, contactKey);
            }

            StoredMessageDirection direction;
            if (ownPersonId is not null && authorId == ownPersonId)
            {
                direction = StoredMessageDirection.Outgoing;
            }
            else if (authorId == contactPersonId)
            {
                direction = StoredMessageDirection.Incoming;
            }
            else
            {
                throw StoredValues.Inconsistent("a one-to-one message has an author who is neither the user nor the contact");
            }

            long? state = reader.IsDBNull(11) ? null : reader.GetInt64(11);
            StoredDeliveryState delivery = DeliveryOf(direction, state, reader.GetInt64(12));

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

            messages.Add(new StoredMessage(contactKey, direction, id, reader.GetInt64(4), writtenAt, text, recordedAt) { Delivery = delivery });
        }

        return messages;
    }

    private static async Task<ChatRows?> FindChatAsync(SqliteConnection connection, SqliteTransaction transaction, byte[] contactLookup, CancellationToken cancellationToken)
    {
        long? contactPersonId = await Sql.LongAsync(connection, transaction, SelectContactPerson, cancellationToken, ("$identity_key_mac", contactLookup)).ConfigureAwait(false);
        long? ownPersonId = await Sql.LongAsync(connection, transaction, SelectOwnPerson, cancellationToken).ConfigureAwait(false);
        if (contactPersonId is not { } contact || ownPersonId is not { } own)
        {
            return null;
        }

        long? conversationId = await Sql.LongAsync(connection, transaction, SelectConversation, cancellationToken, ("$contact_person_id", contact)).ConfigureAwait(false);
        return conversationId is { } conversation ? new ChatRows(contact, conversation, own) : null;
    }

    private async Task<IReadOnlyDictionary<Ed25519PublicKey, long>> LoadSentSequencesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<Ed25519PublicKey, long> sequences = [];
        using SqliteCommand command = Sql.Command(connection, null, SelectSentSequences);
        using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long conversationId = reader.GetInt64(0);
            long contactPersonId = reader.GetInt64(2);
            Ed25519PublicKey contactKey = StoredValues.IdentityKey(keys, contactPersonId, reader.GetFieldValue<byte[]>(3), reader.GetFieldValue<byte[]>(4));
            RecordLocation location = Locations.ConversationSentSequence(conversationId);
            long sequence = StoredValues.Sequence(location, StoredValues.Open(keys, location, reader.GetFieldValue<byte[]>(1)));
            if (!sequences.TryAdd(contactKey, sequence))
            {
                throw StoredValues.Inconsistent("two chats belong to one contact");
            }
        }

        return sequences;
    }

    private async Task KeepSentSequenceAsync(SqliteConnection connection, SqliteTransaction transaction, ChatRows chat, CancellationToken cancellationToken)
    {
        long? highest = await Sql.LongAsync(
            connection,
            transaction,
            SelectHighestOwnSequence,
            cancellationToken,
            ("$conversation_id", chat.ConversationId),
            ("$author_person_id", chat.OwnPersonId)).ConfigureAwait(false);
        if (highest is not { } sent)
        {
            return;
        }

        RecordLocation location = Locations.ConversationSentSequence(chat.ConversationId);
        using (SqliteCommand read = Sql.Command(connection, transaction, SelectSentSequence, ("$conversation_id", chat.ConversationId)))
        {
            if (await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is byte[] sealedSequence
                && StoredValues.Sequence(location, StoredValues.Open(keys, location, sealedSequence)) >= sent)
            {
                return;
            }
        }

        await Sql.ExecuteAsync(
            connection,
            transaction,
            UpdateSentSequence,
            cancellationToken,
            ("$sent_sequence_enc", keys.Seal(location, StoredValues.Sequence(sent))),
            ("$conversation_id", chat.ConversationId)).ConfigureAwait(false);
    }

    private async Task<bool> AddAsync(SqliteConnection connection, SqliteTransaction transaction, StoredMessage message, CancellationToken cancellationToken)
    {
        byte[] contactLookup = keys.Lookup(LookupDomain.IdentityKey, message.ContactKey.Bytes);
        long contactPersonId = await Sql.LongAsync(connection, transaction, SelectContactPerson, cancellationToken, ("$identity_key_mac", contactLookup)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The message's contact is not stored.");

        long ownPersonId = await Sql.LongAsync(connection, transaction, SelectOwnPerson, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No identity is stored, so the message has no owner.");

        await Sql.ExecuteAsync(connection, transaction, InsertConversation, cancellationToken, ("$contact_person_id", contactPersonId)).ConfigureAwait(false);
        long conversationId = await Sql.LongAsync(connection, transaction, SelectConversation, cancellationToken, ("$contact_person_id", contactPersonId)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The conversation could not be stored.");

        long rowId = await Sql.LongAsync(connection, transaction, NextMessageId, cancellationToken).ConfigureAwait(false) ?? 1;
        bool outgoing = message.Direction == StoredMessageDirection.Outgoing;
        long authorId = outgoing ? ownPersonId : contactPersonId;
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
            ("$recorded_at_enc", keys.Seal(Locations.MessageRecordedAt(rowId), StoredValues.Time(message.RecordedAt)))).ConfigureAwait(false);

        if (outgoing)
        {
            await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertDelivery,
                cancellationToken,
                ("$message_id", rowId),
                ("$recipient_person_id", contactPersonId),
                ("$state", (long)message.Delivery)).ConfigureAwait(false);
        }

        await Sql.ExecuteAsync(connection, transaction, InsertSeen, cancellationToken, ("$message_id_mac", idLookup)).ConfigureAwait(false);
        return true;
    }

    private readonly record struct ChatRows(long ContactPersonId, long ConversationId, long OwnPersonId);
}
