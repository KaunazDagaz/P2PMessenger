using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class GroupDeletionTests
{
    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteConversation_RemovesTheGroupsMessagesOnlyAndKeepsTheGroupItsMembersAndTheSeenIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var own = GroupSamples.Message(record, key, user, 1, "Hello team", 3, bob.IdentityKey, carol.IdentityKey);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "Hi all", 4);
            Assert.True(await store.Data.GroupMessages.AddAsync(own, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
            long deliveries;
            await using (var before = await store.RawAsync(cancellationToken))
            {
                deliveries = await Sql.ScalarAsync(before, "SELECT count(*) FROM message_delivery;", cancellationToken);
            }

            var removed = await store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [.. Sorted(Range(bob, 1, 1), Range(user, 1, 1))], cancellationToken);

            Assert.Equal(2, removed);
            await store.ReopenAsync(Samples.DataKey(), cancellationToken);
            Assert.Empty(await store.Data.GroupMessages.LoadAllAsync(cancellationToken));
            Assert.Equal(3, (await store.Data.Messages.LoadAllAsync(cancellationToken)).Count);
            var group = Assert.Single(await store.Data.Groups.LoadAllAsync(cancellationToken));
            group.Key.Dispose();
            Assert.Equal(record, group.Record);
            Assert.Equal(3, group.Members.Count);
            Assert.True(await store.Data.SeenMessages.ContainsAsync(own.Id, cancellationToken));
            Assert.True(await store.Data.SeenMessages.ContainsAsync(fromBob.Id, cancellationToken));
            await using var raw = await store.RawAsync(cancellationToken);
            Assert.Equal(deliveries - 2, await Sql.ScalarAsync(raw, "SELECT count(*) FROM message_delivery;", cancellationToken));
            Assert.Empty(await Sql.StringsAsync(raw, "PRAGMA foreign_key_check;", cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("Story", "US-13")]
    public async Task DeletedRanges_KeptAcrossAReopen_AreLoadedForTheirGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (first, firstKey) = GroupSamples.Record(user, "First", (bob, "Bob"));
        var (second, secondKey) = GroupSamples.Record(user, "Second", (carol, "Carol"));
        using (firstKey)
        using (secondKey)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(first, firstKey, user.IdentityKey), cancellationToken);
            await store.Data.Groups.AddAsync(GroupSamples.Group(second, secondKey, user.IdentityKey), cancellationToken);
            Assert.Empty(await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
            AuthorSequenceRange[] ranges = [.. Sorted(Range(bob, 1, 4), Range(bob, 7, 7), Range(user, 1, 2))];

            Assert.Equal(0, await store.Data.GroupMessages.DeleteConversationAsync(first.GroupId, ranges, cancellationToken));
            Assert.Equal(0, await store.Data.GroupMessages.DeleteConversationAsync(second.GroupId, [], cancellationToken));
            await store.ReopenAsync(Samples.DataKey(), cancellationToken);

            var loaded = await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken);
            var kept = Assert.Single(loaded);
            Assert.Equal(first.GroupId, kept.Key);
            Assert.Equal(ranges, kept.Value);
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteConversation_UnknownGroup_DeletesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);

        Assert.Equal(0, await store.Data.GroupMessages.DeleteConversationAsync(GroupId.Create(), [Range(bob, 1, 1)], cancellationToken));
        Assert.False(await store.Data.GroupMessages.DeleteWaitingAsync(GroupId.Create(), Core.Messages.MessageId.Create(), [Range(user, 1, 1)], cancellationToken));
        Assert.Empty(await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    [Trait("Story", "US-21")]
    public async Task DeleteConversation_RangeThatDoesNotStartAtOneOrEndsBeforeItStarts_IsRefused(long first, long last)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [Range(bob, first, last)], cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task DeleteConversation_RangesOutOfOrderOrOverlappingOrTouching_AreRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            AuthorSequenceRange[] backwards = [.. Sorted(Range(bob, 1, 1), Range(user, 1, 1)).Reverse()];

            await Assert.ThrowsAsync<ArgumentException>(() => store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, backwards, cancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [Range(bob, 1, 3), Range(bob, 3, 5)], cancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [Range(bob, 1, 3), Range(bob, 4, 5)], cancellationToken));
            Assert.Equal(0, await store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [Range(bob, 1, 3), Range(bob, 5, 5)], cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-21")]
    public async Task DeleteWaiting_OwnMessageNoMemberHasYet_IsRemovedWithItsDeliveriesAndItsNumberKept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var kept = GroupSamples.Message(record, key, user, 1, "Keep", 3, bob.IdentityKey, carol.IdentityKey);
            var dropped = GroupSamples.Message(record, key, user, 2, "Drop", 4, bob.IdentityKey, carol.IdentityKey);
            Assert.True(await store.Data.GroupMessages.AddAsync(kept, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(dropped, cancellationToken));

            Assert.True(await store.Data.GroupMessages.DeleteWaitingAsync(record.GroupId, dropped.Id, [Range(user, 2, 2)], cancellationToken));

            await store.ReopenAsync(Samples.DataKey(), cancellationToken);
            var left = Assert.Single(await store.Data.GroupMessages.LoadAllAsync(cancellationToken));
            Assert.Equal(kept.Id, left.Id);
            Assert.Equal(2, left.Deliveries.Count);
            Assert.Equal([Range(user, 2, 2)], Assert.Single(await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken)).Value);
            Assert.True(await store.Data.SeenMessages.ContainsAsync(dropped.Id, cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-15")]
    [Trait("Story", "US-16")]
    public async Task DeleteWaiting_MessageAMemberHasOrAnotherMembersOrInAnotherGroup_IsNotDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        var (other, otherKey) = GroupSamples.Record(user, "Other", (bob, "Bob"));
        using (key)
        using (otherKey)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            await store.Data.Groups.AddAsync(GroupSamples.Group(other, otherKey, user.IdentityKey), cancellationToken);
            var halfway = GroupSamples.Message(record, key, user, 1, "Halfway", 3, bob.IdentityKey, carol.IdentityKey);
            var fromBob = GroupSamples.Message(record, key, bob, 1, "Hi all", 4);
            var waiting = GroupSamples.Message(record, key, user, 2, "Waiting", 5, bob.IdentityKey, carol.IdentityKey);
            Assert.True(await store.Data.GroupMessages.AddAsync(halfway, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(fromBob, cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(waiting, cancellationToken));
            Assert.True(await store.Data.GroupMessages.MarkReceivedAsync(record.GroupId, carol.IdentityKey, halfway.Id, cancellationToken));

            Assert.False(await store.Data.GroupMessages.DeleteWaitingAsync(record.GroupId, halfway.Id, [Range(user, 1, 1)], cancellationToken));
            Assert.False(await store.Data.GroupMessages.DeleteWaitingAsync(record.GroupId, fromBob.Id, [Range(bob, 1, 1)], cancellationToken));
            Assert.False(await store.Data.GroupMessages.DeleteWaitingAsync(other.GroupId, waiting.Id, [Range(user, 2, 2)], cancellationToken));

            Assert.Equal(3, (await store.Data.GroupMessages.LoadAllAsync(cancellationToken)).Count);
            Assert.Empty(await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-11")]
    public async Task DeleteConversation_LeavesNoStoredTextOfTheGroupsMessagesInTheFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        List<byte[]> deletedTexts;
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            Assert.True(await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, user, 1, "Bridge at six", 3, bob.IdentityKey), cancellationToken));
            Assert.True(await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, bob, 1, "Bring the map", 4), cancellationToken));
            await using (var raw = await store.RawAsync(cancellationToken))
            {
                deletedTexts = [.. await BlobsAsync(raw, "SELECT text_enc FROM message WHERE group_envelope_enc IS NOT NULL;", cancellationToken), .. await BlobsAsync(raw, "SELECT group_envelope_enc FROM message WHERE group_envelope_enc IS NOT NULL;", cancellationToken)];
            }

            Assert.Equal(4, deletedTexts.Count);
            await store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [.. Sorted(Range(user, 1, 1), Range(bob, 1, 1))], cancellationToken);
        }

        foreach (var path in new[] { store.FilePath, store.FilePath + "-wal", store.FilePath + "-shm" }.Where(File.Exists))
        {
            var content = await ReadSharedAsync(path, cancellationToken);
            foreach (var text in deletedTexts)
            {
                Assert.Equal(-1, content.AsSpan().IndexOf(text));
            }
        }
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    [Trait("NFR", "NFR-11")]
    public async Task LoadDeletedRanges_EveryFlippedBitOfTheStoredRanges_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            await store.Data.GroupMessages.DeleteConversationAsync(record.GroupId, [Range(bob, 1, 2)], cancellationToken);
        }

        await using var raw = await store.RawAsync(cancellationToken);
        var original = Assert.Single(await BlobsAsync(raw, "SELECT deleted_ranges_enc FROM conversation WHERE deleted_ranges_enc IS NOT NULL;", cancellationToken));
        for (var bit = 0; bit < original.Length * 8; bit++)
        {
            var changed = (byte[])original.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            await Sql.ExecuteAsync(raw, "UPDATE conversation SET deleted_ranges_enc = $value WHERE deleted_ranges_enc IS NOT NULL;", [("$value", changed)], cancellationToken);

            await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
        }

        await Sql.ExecuteAsync(raw, "UPDATE conversation SET deleted_ranges_enc = $value WHERE deleted_ranges_enc IS NOT NULL;", [("$value", original)], cancellationToken);
        Assert.Single(await store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    [Trait("NFR", "NFR-09")]
    public async Task LoadDeletedRanges_MovedToAnotherGroupsConversation_IsRefusedAsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (first, firstKey) = GroupSamples.Record(user, "First", (bob, "Bob"));
        var (second, secondKey) = GroupSamples.Record(user, "Second", (carol, "Carol"));
        using (firstKey)
        using (secondKey)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(first, firstKey, user.IdentityKey), cancellationToken);
            await store.Data.Groups.AddAsync(GroupSamples.Group(second, secondKey, user.IdentityKey), cancellationToken);
            await store.Data.GroupMessages.DeleteConversationAsync(first.GroupId, [Range(bob, 1, 1)], cancellationToken);
        }

        await using var raw = await store.RawAsync(cancellationToken);
        await Sql.ExecuteAsync(
            raw,
            "UPDATE conversation SET deleted_ranges_enc = (SELECT deleted_ranges_enc FROM conversation WHERE deleted_ranges_enc IS NOT NULL) WHERE group_id = (SELECT id FROM chat_group ORDER BY id DESC LIMIT 1);",
            [],
            cancellationToken);
        await Sql.ExecuteAsync(raw, "UPDATE conversation SET deleted_ranges_enc = NULL WHERE group_id = (SELECT id FROM chat_group ORDER BY id LIMIT 1);", [], cancellationToken);

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.GroupMessages.LoadDeletedRangesAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-21")]
    public async Task Schema_DeletedRangesShorterThanASealedValue_AreRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
        }

        await using var raw = await store.RawAsync(cancellationToken);
        await Assert.ThrowsAsync<SqliteException>(() => Sql.ExecuteAsync(raw, "UPDATE conversation SET deleted_ranges_enc = x'00' WHERE group_id IS NOT NULL;", [], cancellationToken));
    }

    private static AuthorSequenceRange Range(KeySet author, long first, long last) => new(author.IdentityKey, first, last);

    private static IEnumerable<AuthorSequenceRange> Sorted(params AuthorSequenceRange[] ranges) =>
        ranges.OrderBy(static range => Convert.ToHexString(range.AuthorKey.Bytes), StringComparer.Ordinal).ThenBy(static range => range.First);

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
