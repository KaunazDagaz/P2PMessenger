using System.Text;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

public sealed class CopiedConversationRepository
{
    private const string SelectConversations =
        "SELECT id, origin_mac, origin_enc, title_enc, copied_at_enc FROM copied_conversation ORDER BY id;";

    private const string SelectMessages =
        "SELECT copied_conversation_id, position, written_by_owner, author_name_enc, text_enc, written_at_enc "
        + "FROM copied_message ORDER BY copied_conversation_id, position;";

    private const string SelectByOrigin = "SELECT id FROM copied_conversation WHERE origin_mac = $origin_mac;";

    private const string DeleteById = "DELETE FROM copied_conversation WHERE id = $id;";

    private const string NextConversationId = "SELECT coalesce(max(id), 0) + 1 FROM copied_conversation;";

    private const string InsertConversation =
        "INSERT INTO copied_conversation (id, origin_mac, origin_enc, title_enc, copied_at_enc) "
        + "VALUES ($id, $origin_mac, $origin_enc, $title_enc, $copied_at_enc);";

    private const string InsertMessage =
        "INSERT INTO copied_message (copied_conversation_id, position, written_by_owner, author_name_enc, text_enc, written_at_enc) "
        + "VALUES ($copied_conversation_id, $position, $written_by_owner, $author_name_enc, $text_enc, $written_at_enc);";

    private readonly SqliteGate gate;
    private readonly RecordKeys keys;

    internal CopiedConversationRepository(SqliteGate gate, RecordKeys keys)
    {
        this.gate = gate;
        this.keys = keys;
    }

    public Task<IReadOnlyList<StoredCopiedConversation>> LoadAllAsync(CancellationToken cancellationToken) =>
        gate.ReadAsync(LoadAllAsync, cancellationToken);

    public Task SaveAsync(StoredCopiedConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(conversation.Origin);
        ArgumentNullException.ThrowIfNull(conversation.Title);
        ArgumentNullException.ThrowIfNull(conversation.Messages);
        ValidateMessages(conversation.Messages, nameof(conversation));

        return gate.WriteAsync((connection, transaction, token) => SaveAsync(connection, transaction, conversation, token), cancellationToken);
    }

    public async Task<bool> DeleteAsync(CopyOriginId origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        byte[] lookup = keys.Lookup(LookupDomain.CopyOrigin, origin.Bytes);
        bool deleted = await gate.WriteAsync(
            async (connection, transaction, token) =>
            {
                long? id = await Sql.LongAsync(connection, transaction, SelectByOrigin, token, ("$origin_mac", lookup)).ConfigureAwait(false);
                if (id is null)
                {
                    return false;
                }

                return await Sql.ExecuteAsync(connection, transaction, DeleteById, token, ("$id", id.Value)).ConfigureAwait(false) == 1;
            },
            cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await gate.TruncateJournalAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static void ValidateMessages(IReadOnlyList<StoredCopiedMessage> messages, string parameterName)
    {
        if (messages.Count > CopyConversationStart.MaxMessages)
        {
            throw new ArgumentException("The copied conversation exceeds the maximum number of messages.", parameterName);
        }

        for (int index = 0; index < messages.Count; index++)
        {
            StoredCopiedMessage message = messages[index];
            ArgumentNullException.ThrowIfNull(message, parameterName);
            if (message.Position != index + 1)
            {
                throw new ArgumentException("Copied messages must have contiguous positions starting at 1.", parameterName);
            }

            if (message.WrittenByOwner ? message.AuthorName is not null : message.AuthorName is null)
            {
                throw new ArgumentException("Only messages written by others carry an author name.", parameterName);
            }

            if (string.IsNullOrEmpty(message.Text) || Encoding.UTF8.GetByteCount(message.Text) > TextMessage.MaxUtf8Length)
            {
                throw new ArgumentException("Copied message text must be non-empty and within the maximum UTF-8 length.", parameterName);
            }
        }
    }

    private async Task<IReadOnlyList<StoredCopiedConversation>> LoadAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<long, (CopyOriginId Origin, DisplayName Title, DateTimeOffset CopiedAt, List<StoredCopiedMessage> Messages)> conversations = [];
        List<long> order = [];
        using (SqliteCommand command = Sql.Command(connection, null, SelectConversations))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long id = reader.GetInt64(0);
                RecordLocation originLocation = Locations.CopiedConversationOrigin(id);
                byte[] originBytes = StoredValues.Open(keys, originLocation, reader.GetFieldValue<byte[]>(2));
                if (!CopyOriginId.TryCreate(originBytes, out CopyOriginId? origin)
                    || !keys.MatchesLookup(LookupDomain.CopyOrigin, originBytes, reader.GetFieldValue<byte[]>(1)))
                {
                    throw StoredValues.Damaged(originLocation);
                }

                RecordLocation titleLocation = Locations.CopiedConversationTitle(id);
                string titleText = StoredValues.Text(titleLocation, StoredValues.Open(keys, titleLocation, reader.GetFieldValue<byte[]>(3)));
                if (!DisplayName.TryCreate(titleText, out DisplayName? title) || !string.Equals(title.Value, titleText, StringComparison.Ordinal))
                {
                    throw StoredValues.Damaged(titleLocation);
                }

                RecordLocation copiedAtLocation = Locations.CopiedConversationCopiedAt(id);
                DateTimeOffset copiedAt = StoredValues.Time(copiedAtLocation, StoredValues.Open(keys, copiedAtLocation, reader.GetFieldValue<byte[]>(4)));
                conversations.Add(id, (origin, title, copiedAt, []));
                order.Add(id);
            }
        }

        using (SqliteCommand command = Sql.Command(connection, null, SelectMessages))
        using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long conversationId = reader.GetInt64(0);
                if (!conversations.TryGetValue(conversationId, out (CopyOriginId Origin, DisplayName Title, DateTimeOffset CopiedAt, List<StoredCopiedMessage> Messages) conversation))
                {
                    throw StoredValues.Inconsistent("a copied message belongs to no copied conversation");
                }

                long positionValue = reader.GetInt64(1);
                if (positionValue != conversation.Messages.Count + 1 || positionValue > CopyConversationStart.MaxMessages)
                {
                    throw StoredValues.Inconsistent("copied messages are not numbered continuously from 1");
                }

                int position = (int)positionValue;
                long ownerFlag = reader.GetInt64(2);
                if (ownerFlag is not (0 or 1))
                {
                    throw StoredValues.Inconsistent("a copied message has an unknown owner flag");
                }

                bool writtenByOwner = ownerFlag == 1;
                DisplayName? authorName = null;
                if (writtenByOwner)
                {
                    if (!reader.IsDBNull(3))
                    {
                        throw StoredValues.Inconsistent("an owner copied message carries an author name");
                    }
                }
                else
                {
                    if (reader.IsDBNull(3))
                    {
                        throw StoredValues.Inconsistent("a copied message from another person lacks an author name");
                    }

                    RecordLocation authorLocation = Locations.CopiedMessageAuthorName(conversationId, position, writtenByOwner);
                    string authorText = StoredValues.Text(authorLocation, StoredValues.Open(keys, authorLocation, reader.GetFieldValue<byte[]>(3)));
                    if (!DisplayName.TryCreate(authorText, out authorName) || !string.Equals(authorName.Value, authorText, StringComparison.Ordinal))
                    {
                        throw StoredValues.Damaged(authorLocation);
                    }
                }

                RecordLocation textLocation = Locations.CopiedMessageText(conversationId, position, writtenByOwner);
                byte[] textBytes = StoredValues.Open(keys, textLocation, reader.GetFieldValue<byte[]>(4));
                if (textBytes.Length is 0 or > TextMessage.MaxUtf8Length)
                {
                    throw StoredValues.Damaged(textLocation);
                }

                string text = StoredValues.Text(textLocation, textBytes);

                RecordLocation writtenAtLocation = Locations.CopiedMessageWrittenAt(conversationId, position, writtenByOwner);
                DateTimeOffset writtenAt = StoredValues.Time(writtenAtLocation, StoredValues.Open(keys, writtenAtLocation, reader.GetFieldValue<byte[]>(5)));

                conversation.Messages.Add(new StoredCopiedMessage(position, writtenByOwner, authorName, text, writtenAt));
            }
        }

        List<StoredCopiedConversation> result = new(order.Count);
        foreach (long id in order)
        {
            (CopyOriginId origin, DisplayName title, DateTimeOffset copiedAt, List<StoredCopiedMessage> messages) = conversations[id];
            result.Add(new StoredCopiedConversation(origin, title, copiedAt, messages));
        }

        return result;
    }

    private async Task<bool> SaveAsync(SqliteConnection connection, SqliteTransaction transaction, StoredCopiedConversation conversation, CancellationToken cancellationToken)
    {
        byte[] lookup = keys.Lookup(LookupDomain.CopyOrigin, conversation.Origin.Bytes);
        long? existing = await Sql.LongAsync(connection, transaction, SelectByOrigin, cancellationToken, ("$origin_mac", lookup)).ConfigureAwait(false);
        if (existing is not null)
        {
            _ = await Sql.ExecuteAsync(connection, transaction, DeleteById, cancellationToken, ("$id", existing.Value)).ConfigureAwait(false);
        }

        long id = await Sql.LongAsync(connection, transaction, NextConversationId, cancellationToken).ConfigureAwait(false) ?? 1;
        _ = await Sql.ExecuteAsync(
            connection,
            transaction,
            InsertConversation,
            cancellationToken,
            ("$id", id),
            ("$origin_mac", lookup),
            ("$origin_enc", keys.Seal(Locations.CopiedConversationOrigin(id), conversation.Origin.Bytes)),
            ("$title_enc", keys.Seal(Locations.CopiedConversationTitle(id), StoredValues.Utf8(conversation.Title.Value))),
            ("$copied_at_enc", keys.Seal(Locations.CopiedConversationCopiedAt(id), StoredValues.Time(conversation.CopiedAt)))).ConfigureAwait(false);

        foreach (StoredCopiedMessage message in conversation.Messages)
        {
            object authorEnc = message.WrittenByOwner
                ? DBNull.Value
                : keys.Seal(Locations.CopiedMessageAuthorName(id, message.Position, message.WrittenByOwner), StoredValues.Utf8(message.AuthorName!.Value));
            _ = await Sql.ExecuteAsync(
                connection,
                transaction,
                InsertMessage,
                cancellationToken,
                ("$copied_conversation_id", id),
                ("$position", (long)message.Position),
                ("$written_by_owner", message.WrittenByOwner ? 1L : 0L),
                ("$author_name_enc", authorEnc),
                ("$text_enc", keys.Seal(Locations.CopiedMessageText(id, message.Position, message.WrittenByOwner), StoredValues.Utf8(message.Text))),
                ("$written_at_enc", keys.Seal(Locations.CopiedMessageWrittenAt(id, message.Position, message.WrittenByOwner), StoredValues.Time(message.WrittenAt)))).ConfigureAwait(false);
        }

        return true;
    }
}
