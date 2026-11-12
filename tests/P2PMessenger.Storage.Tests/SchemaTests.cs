using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests;

public sealed class SchemaTests
{
    private const int ConstraintFailed = 19;

    private static readonly string[] DocumentedTables =
    [
        "chat_group",
        "contact",
        "conversation",
        "copied_conversation",
        "copied_message",
        "data_key",
        "group_key",
        "group_member",
        "identity",
        "message",
        "message_delivery",
        "person",
        "seen_message",
    ];

    private static readonly byte[] Sealed = new byte[40];

    [Fact]
    public async Task Schema_Tables_AreTheDocumentedOnesAndAllStrict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);

        var tables = await Sql.StringsAsync(connection, "SELECT name FROM pragma_table_list WHERE schema = 'main' AND type = 'table' AND name NOT LIKE 'sqlite%' ORDER BY name;", cancellationToken);
        var notStrict = await Sql.StringsAsync(connection, "SELECT name FROM pragma_table_list WHERE schema = 'main' AND type = 'table' AND name NOT LIKE 'sqlite%' AND strict = 0;", cancellationToken);

        Assert.Equal(DocumentedTables, tables);
        Assert.Empty(notStrict);
    }

    [Fact]
    public async Task Schema_NewDatabase_PassesTheIntegrityAndForeignKeyChecks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);

        Assert.Equal("ok", Assert.Single(await Sql.StringsAsync(connection, "PRAGMA integrity_check;", cancellationToken)));
        Assert.Empty(await Sql.StringsAsync(connection, "PRAGMA foreign_key_check;", cancellationToken));
    }

    [Fact]
    public async Task Schema_MessageInAMissingConversation_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);
        await AddUserAndContactAsync(connection, cancellationToken);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => AddMessageAsync(connection, 99, cancellationToken));

        Assert.Equal(ConstraintFailed, exception.SqliteErrorCode);
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-15")]
    public async Task Schema_DeletingAChat_DeletesItsMessagesAndCancelsTheWaitingOnes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);
        await AddUserAndContactAsync(connection, cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO conversation (id, contact_person_id) VALUES (1, 2);", [], cancellationToken);
        await AddMessageAsync(connection, 1, cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO message_delivery (message_id, recipient_person_id, state) VALUES (1, 2, 1);", [], cancellationToken);
        Assert.Equal(1, await Sql.ScalarAsync(connection, "SELECT count(*) FROM outbox;", cancellationToken));

        await Sql.ExecuteAsync(connection, "DELETE FROM conversation WHERE id = 1;", [], cancellationToken);

        Assert.Equal(0, await Sql.ScalarAsync(connection, "SELECT count(*) FROM message;", cancellationToken));
        Assert.Equal(0, await Sql.ScalarAsync(connection, "SELECT count(*) FROM message_delivery;", cancellationToken));
        Assert.Equal(0, await Sql.ScalarAsync(connection, "SELECT count(*) FROM outbox;", cancellationToken));
        Assert.Equal(1, await Sql.ScalarAsync(connection, "SELECT count(*) FROM contact;", cancellationToken));
    }

    [Fact]
    public async Task Schema_MacOfTheWrongLength_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => Sql.ExecuteAsync(connection, "INSERT INTO seen_message (message_id_mac) VALUES ($mac);", [("$mac", new byte[31])], cancellationToken));

        Assert.Equal(ConstraintFailed, exception.SqliteErrorCode);
    }

    [Fact]
    public async Task Schema_ConversationWithBothAContactAndAGroup_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var database = new TemporaryDatabase();
        await using var connection = await database.OpenMigratedAsync(cancellationToken);
        await AddUserAndContactAsync(connection, cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO chat_group (id, shared_id_mac, shared_id_enc, name_enc) VALUES (1, $mac, $sealed, $sealed);", [("$mac", Mac(9)), ("$sealed", Sealed)], cancellationToken);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => Sql.ExecuteAsync(connection, "INSERT INTO conversation (contact_person_id, group_id) VALUES (2, 1);", [], cancellationToken));

        Assert.Equal(ConstraintFailed, exception.SqliteErrorCode);
    }

    private static async Task AddUserAndContactAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await Sql.ExecuteAsync(connection, "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) VALUES (1, $mac, $sealed, $sealed);", [("$mac", Mac(1)), ("$sealed", Sealed)], cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO person (id, identity_key_mac, identity_key_enc, display_name_enc) VALUES (2, $mac, $sealed, $sealed);", [("$mac", Mac(2)), ("$sealed", Sealed)], cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO identity (id, person_id, key_set_enc) VALUES (1, 1, $sealed);", [("$sealed", Sealed)], cancellationToken);
        await Sql.ExecuteAsync(connection, "INSERT INTO contact (person_id, code_version, static_key_mac, static_key_enc, onion_key_enc, code_signature_enc, added_via, status) VALUES (2, 1, $mac, $sealed, $sealed, $sealed, 2, 1);", [("$mac", Mac(3)), ("$sealed", Sealed)], cancellationToken);
    }

    private static Task<int> AddMessageAsync(SqliteConnection connection, long conversationId, CancellationToken cancellationToken) =>
        Sql.ExecuteAsync(connection, "INSERT INTO message (id, conversation_id, author_person_id, message_id_mac, sender_sequence, text_enc, written_at_enc) VALUES (1, $conversation, 1, $mac, 1, $sealed, $sealed);", [("$conversation", conversationId), ("$mac", Mac(5)), ("$sealed", Sealed)], cancellationToken);

    private static byte[] Mac(byte fill)
    {
        var mac = new byte[32];
        Array.Fill(mac, fill);
        return mac;
    }
}
