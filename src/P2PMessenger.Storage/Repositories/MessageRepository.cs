using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class MessageRepository
{
    private const string SelectMessages =
        "SELECT message.id, message.author_person_id, message.message_id_mac, message.message_id_enc, message.sender_sequence, message.text_enc, "
        + "message.written_at_enc, message.recorded_at_enc, conversation.contact_person_id, person.identity_key_mac, person.identity_key_enc "
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

        return gate.WriteAsync((connection, transaction, token) => AddAsync(connection, transaction, message, token), cancellationToken);
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

            messages.Add(new StoredMessage(contactKey, direction, id, reader.GetInt64(4), writtenAt, text, recordedAt));
        }

        return messages;
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
        long authorId = message.Direction == StoredMessageDirection.Outgoing ? ownPersonId : contactPersonId;

        await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertMessage,
            cancellationToken,
            ("$id", rowId),
            ("$conversation_id", conversationId),
            ("$author_person_id", authorId),
            ("$message_id_mac", keys.Lookup(LookupDomain.MessageId, message.Id.Bytes)),
            ("$message_id_enc", keys.Seal(Locations.MessageId(rowId), message.Id.Bytes)),
            ("$sender_sequence", message.Sequence),
            ("$text_enc", keys.Seal(Locations.MessageText(rowId), StoredValues.Utf8(message.Text))),
            ("$written_at_enc", keys.Seal(Locations.MessageWrittenAt(rowId), StoredValues.Time(message.WrittenAt))),
            ("$recorded_at_enc", keys.Seal(Locations.MessageRecordedAt(rowId), StoredValues.Time(message.RecordedAt)))).ConfigureAwait(false);

        return true;
    }
}
