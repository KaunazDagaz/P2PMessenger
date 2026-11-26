using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class CopiedConversationRepositoryTests
{
    public static TheoryData<string, string, string, long> TamperColumns { get; } = new()
    {
        { "copied_conversation", "origin_mac", "id", 1 },
        { "copied_conversation", "origin_enc", "id", 1 },
        { "copied_conversation", "title_enc", "id", 1 },
        { "copied_conversation", "copied_at_enc", "id", 1 },
        { "copied_message", "text_enc", "position", 1 },
        { "copied_message", "written_at_enc", "position", 1 },
        { "copied_message", "author_name_enc", "position", 2 },
        { "copied_message", "text_enc", "position", 2 },
        { "copied_message", "written_at_enc", "position", 2 },
    };

    public static TheoryData<string> TamperMoves { get; } =
    [
        "UPDATE copied_message SET text_enc = (SELECT text_enc FROM copied_message WHERE copied_conversation_id = 1 AND position = 2) WHERE copied_conversation_id = 1 AND position = 1;",
        "UPDATE copied_message SET author_name_enc = (SELECT title_enc FROM copied_conversation WHERE id = 1) WHERE copied_conversation_id = 1 AND position = 2;",
        "UPDATE copied_conversation SET title_enc = (SELECT title_enc FROM copied_conversation WHERE id = 2) WHERE id = 1;",
        "UPDATE copied_message SET position = 5 WHERE copied_conversation_id = 1 AND position = 2;",
        "UPDATE copied_message SET written_by_owner = 1, author_name_enc = NULL WHERE copied_conversation_id = 1 AND position = 2;",
        "UPDATE copied_message SET written_by_owner = 0, author_name_enc = (SELECT author_name_enc FROM copied_message WHERE copied_conversation_id = 1 AND position = 2) WHERE copied_conversation_id = 1 AND position = 1;",
    ];

    [Fact]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-11")]
    public async Task SaveLoadReplaceAndDelete_RoundTripsContactAndGroupCopiesAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        using var phoneOwner = KeySet.Create();
        using var bob = KeySet.Create();
        var contactOrigin = CopyOriginId.ForContact(phoneOwner.IdentityKey, bob.IdentityKey);
        var groupOrigin = CopyOriginId.ForGroup(phoneOwner.IdentityKey, GroupId.Create());

        var firstContact = new StoredCopiedConversation(
            contactOrigin,
            Samples.Name("Bob"),
            Samples.WrittenAt,
            [
                new StoredCopiedMessage(1, true, null, "Hello Bob", Samples.WrittenAt),
                new StoredCopiedMessage(2, false, Samples.Name("Bob"), "Hi Alice", Samples.WrittenAt.AddMinutes(1)),
            ]);
        var groupCopy = new StoredCopiedConversation(
            groupOrigin,
            Samples.Name("Friends"),
            Samples.WrittenAt.AddMinutes(5),
            [
                new StoredCopiedMessage(1, false, Samples.Name("Carol"), "Welcome!", Samples.WrittenAt.AddMinutes(2)),
            ]);

        await store.Data.CopiedConversations.SaveAsync(firstContact, cancellationToken);
        await store.Data.CopiedConversations.SaveAsync(groupCopy, cancellationToken);

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = await store.Data.CopiedConversations.LoadAllAsync(cancellationToken);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(contactOrigin, loaded[0].Origin);
        Assert.Equal("Bob", loaded[0].Title.Value);
        Assert.Equal(2, loaded[0].Messages.Count);
        Assert.True(loaded[0].Messages[0].WrittenByOwner);
        Assert.Null(loaded[0].Messages[0].AuthorName);
        Assert.Equal("Bob", loaded[0].Messages[1].AuthorName?.Value);

        var updatedContact = new StoredCopiedConversation(
            contactOrigin,
            Samples.Name("Bob"),
            Samples.WrittenAt.AddHours(2),
            [
                new StoredCopiedMessage(1, true, null, "Hello Bob", Samples.WrittenAt),
                new StoredCopiedMessage(2, false, Samples.Name("Bob"), "Hi Alice", Samples.WrittenAt.AddMinutes(1)),
                new StoredCopiedMessage(3, true, null, "Newer message", Samples.WrittenAt.AddHours(1)),
            ]);
        await store.Data.CopiedConversations.SaveAsync(updatedContact, cancellationToken);

        var afterReplace = await store.Data.CopiedConversations.LoadAllAsync(cancellationToken);
        Assert.Equal(2, afterReplace.Count);
        var replaced = Assert.Single(afterReplace, item => item.Origin.Equals(contactOrigin));
        Assert.Equal(3, replaced.Messages.Count);
        Assert.Equal("Newer message", replaced.Messages[2].Text);
        Assert.Contains(afterReplace, item => item.Origin.Equals(groupOrigin));

        Assert.True(await store.Data.CopiedConversations.DeleteAsync(contactOrigin, cancellationToken));
        Assert.False(await store.Data.CopiedConversations.DeleteAsync(contactOrigin, cancellationToken));
        var remaining = await store.Data.CopiedConversations.LoadAllAsync(cancellationToken);
        Assert.Single(remaining);
        Assert.Equal(groupOrigin, remaining[0].Origin);
    }

    [Theory]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    [MemberData(nameof(TamperColumns))]
    public async Task LoadAll_EveryFlippedBitOfACopiedValue_IsRefusedAsDamaged(string table, string column, string keyColumn, long key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        await SeedTwoCopiesAsync(store, cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);
        var original = await ReadAsync(raw, table, column, keyColumn, key, cancellationToken);

        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            await WriteAsync(raw, table, column, keyColumn, key, changed, cancellationToken);

            await Assert.ThrowsAsync<StoredDataException>(() => store.Data.CopiedConversations.LoadAllAsync(cancellationToken));
        }

        await WriteAsync(raw, table, column, keyColumn, key, original, cancellationToken);
        Assert.Equal(2, (await store.Data.CopiedConversations.LoadAllAsync(cancellationToken)).Count);
    }

    [Theory]
    [Trait("Story", "US-22")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(TamperMoves))]
    public async Task LoadAll_CopiedValueMovedOrFlagChanged_IsRefusedAsDamaged(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        await SeedTwoCopiesAsync(store, cancellationToken);
        await using var raw = await store.RawAsync(cancellationToken);

        await Sql.ExecuteAsync(raw, sql, [], cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.CopiedConversations.LoadAllAsync(cancellationToken));
    }

    private static async Task SeedTwoCopiesAsync(StorageFixture store, CancellationToken cancellationToken)
    {
        using var owner = KeySet.Create();
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        await store.Data.CopiedConversations.SaveAsync(
            new StoredCopiedConversation(
                CopyOriginId.ForContact(owner.IdentityKey, bob.IdentityKey),
                Samples.Name("Bob"),
                Samples.WrittenAt,
                [
                    new StoredCopiedMessage(1, true, null, "Message one", Samples.WrittenAt),
                    new StoredCopiedMessage(2, false, Samples.Name("Bob"), "Message two", Samples.WrittenAt.AddMinutes(1)),
                ]),
            cancellationToken);
        await store.Data.CopiedConversations.SaveAsync(
            new StoredCopiedConversation(
                CopyOriginId.ForContact(owner.IdentityKey, carol.IdentityKey),
                Samples.Name("Carol"),
                Samples.WrittenAt.AddMinutes(5),
                [
                    new StoredCopiedMessage(1, false, Samples.Name("Carol"), "Message three", Samples.WrittenAt.AddMinutes(2)),
                ]),
            cancellationToken);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table and column names come from the constant theory data of this class.")]
    private static async Task<byte[]> ReadAsync(SqliteConnection connection, string table, string column, string keyColumn, long key, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = table == "copied_message"
            ? $"SELECT {column} FROM {table} WHERE copied_conversation_id = 1 AND {keyColumn} = $key;"
            : $"SELECT {column} FROM {table} WHERE {keyColumn} = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (byte[])(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("No such row."));
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table and column names come from the constant theory data of this class.")]
    private static async Task WriteAsync(SqliteConnection connection, string table, string column, string keyColumn, long key, byte[] value, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = table == "copied_message"
            ? $"UPDATE {table} SET {column} = $value WHERE copied_conversation_id = 1 AND {keyColumn} = $key;"
            : $"UPDATE {table} SET {column} = $value WHERE {keyColumn} = $key;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$key", key);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }
}
