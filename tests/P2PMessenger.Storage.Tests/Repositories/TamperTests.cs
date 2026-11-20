using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class TamperTests
{
    public static TheoryData<string, string, string, long> StoredValues { get; } = new()
    {
        { "person", "identity_key_mac", "id", 1 },
        { "person", "identity_key_enc", "id", 1 },
        { "person", "display_name_enc", "id", 1 },
        { "person", "identity_key_mac", "id", 2 },
        { "person", "identity_key_enc", "id", 2 },
        { "person", "display_name_enc", "id", 2 },
        { "identity", "key_set_enc", "id", 1 },
        { "contact", "static_key_mac", "person_id", 2 },
        { "contact", "static_key_enc", "person_id", 2 },
        { "contact", "onion_key_enc", "person_id", 2 },
        { "contact", "code_signature_enc", "person_id", 2 },
        { "message", "message_id_mac", "id", 1 },
        { "message", "message_id_enc", "id", 1 },
        { "message", "text_enc", "id", 1 },
        { "message", "written_at_enc", "id", 1 },
        { "message", "recorded_at_enc", "id", 1 },
    };

    public static TheoryData<string> Moves { get; } =
    [
        "UPDATE message SET text_enc = (SELECT text_enc FROM message WHERE id = 2) WHERE id = 1;",
        "UPDATE person SET display_name_enc = (SELECT display_name_enc FROM person WHERE id = 2) WHERE id = 1;",
        "UPDATE contact SET onion_key_enc = static_key_enc WHERE person_id = 2;",
        "UPDATE message SET written_at_enc = recorded_at_enc WHERE id = 1;",
        "UPDATE message SET id = 50 WHERE id = 3;",
        "UPDATE contact SET static_key_enc = (SELECT static_key_enc FROM contact WHERE person_id = 3) WHERE person_id = 2;",
        "UPDATE identity SET key_set_enc = (SELECT identity_key_enc FROM person WHERE id = 1);",
    ];

    [Theory]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    [MemberData(nameof(StoredValues))]
    public async Task Load_EveryFlippedBitOfAStoredValue_IsRefusedAsDamaged(string table, string column, string keyColumn, long key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await store.LoadEverythingAsync(cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);
        var original = await ReadAsync(raw, table, column, keyColumn, key, cancellationToken);

        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            await WriteAsync(raw, table, column, keyColumn, key, changed, cancellationToken);

            await Assert.ThrowsAsync<StoredDataException>(() => store.LoadEverythingAsync(cancellationToken));
        }

        await WriteAsync(raw, table, column, keyColumn, key, original, cancellationToken);
        await store.LoadEverythingAsync(cancellationToken);
    }

    [Theory]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(Moves))]
    public async Task Load_StoredValueMovedToAnotherRowOrColumn_IsRefusedAsDamaged(string move)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        await using var raw = await store.RawAsync(cancellationToken);

        await Sql.ExecuteAsync(raw, move, [], cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.LoadEverythingAsync(cancellationToken));
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table and column names come from the constant theory data of this class.")]
    private static async Task<byte[]> ReadAsync(SqliteConnection connection, string table, string column, string keyColumn, long key, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE {keyColumn} = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (byte[])(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("No such row."));
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table and column names come from the constant theory data of this class.")]
    private static async Task WriteAsync(SqliteConnection connection, string table, string column, string keyColumn, long key, byte[] value, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column} = $value WHERE {keyColumn} = $key;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$key", key);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }
}
