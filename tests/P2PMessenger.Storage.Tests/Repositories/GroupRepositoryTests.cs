using Microsoft.Data.Sqlite;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class GroupRepositoryTests
{
    [Fact]
    [Trait("Story", "US-12")]
    public async Task Group_AddedThenLoaded_HasTheSameRecordKeyAndMemberStates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var dave = KeySet.Create();
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"), (dave, "Dave"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
        }

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = Assert.Single(await store.Data.Groups.LoadAllAsync(cancellationToken));
        using var loadedKey = loaded.Key;

        Assert.Equal(record, loaded.Record);
        Assert.True(loaded.Record.Fits(loadedKey));
        Assert.Equal(4, loaded.Members.Count);
        var own = Assert.Single(loaded.Members, member => member.IdentityKey.Equals(user.IdentityKey));
        Assert.Equal(StoredMemberJoin.Joined, own.Joined);
        Assert.All(loaded.Members.Where(member => member != own), member =>
        {
            Assert.Equal(StoredMemberJoin.NotYet, member.Joined);
            Assert.Equal(StoredNoticeState.Waiting, member.Invitation);
        });
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task Group_WithAMemberWhoIsNotAContact_StoresThatPersonWithTheNameFromTheRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, _) = await store.FillAsync(cancellationToken);
        using var dave = KeySet.Create();
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (dave, "Dave"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
        }

        await using var connection = await store.RawAsync(cancellationToken);
        Assert.Equal(4, await Sql.ScalarAsync(connection, "SELECT count(*) FROM person;", cancellationToken));
        Assert.Equal(2, await Sql.ScalarAsync(connection, "SELECT count(*) FROM contact;", cancellationToken));
        Assert.Equal(1, await Sql.ScalarAsync(connection, "SELECT count(*) FROM conversation WHERE group_id IS NOT NULL;", cancellationToken));
        Assert.Equal(2, (await store.Data.Contacts.LoadAllAsync(cancellationToken)).Count);
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task Members_Updated_KeepTheirJoinAndNoticeStatesAcrossAReopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
        }

        var changed = await store.Data.Groups.UpdateMembersAsync(
            record.GroupId,
            [
                new StoredGroupMember(bob.IdentityKey, StoredMemberJoin.Joined, StoredNoticeState.Received, StoredNoticeState.Waiting),
                new StoredGroupMember(carol.IdentityKey, StoredMemberJoin.JoinedAnotherList, StoredNoticeState.Received, StoredNoticeState.Received),
            ],
            cancellationToken);
        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = Assert.Single(await store.Data.Groups.LoadAllAsync(cancellationToken));
        loaded.Key.Dispose();

        Assert.Equal(2, changed);
        Assert.Equal(
            new StoredGroupMember(bob.IdentityKey, StoredMemberJoin.Joined, StoredNoticeState.Received, StoredNoticeState.Waiting),
            loaded.Members.Single(member => member.IdentityKey.Equals(bob.IdentityKey)));
        Assert.Equal(
            new StoredGroupMember(carol.IdentityKey, StoredMemberJoin.JoinedAnotherList, StoredNoticeState.Received, StoredNoticeState.Received),
            loaded.Members.Single(member => member.IdentityKey.Equals(carol.IdentityKey)));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task Group_AddedTwice_IsRefusedByTheDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);

            await Assert.ThrowsAsync<SqliteException>(() => store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task Group_WithAKeyThatDoesNotFitTheRecord_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        key.Dispose();
        using var other = GroupKey.Create();

        await Assert.ThrowsAsync<ArgumentException>(() => store.Data.Groups.AddAsync(GroupSamples.Group(record, other, user.IdentityKey), cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task GroupMessages_AddedThenLoaded_KeepEveryFieldTheSignedEnvelopeAndTheDeliveriesPerMember()
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
            Assert.True(await store.Data.GroupMessages.MarkReceivedAsync(record.GroupId, carol.IdentityKey, own.Id, cancellationToken));

            await store.ReopenAsync(Samples.DataKey(), cancellationToken);
            var loaded = await store.Data.GroupMessages.LoadAllAsync(cancellationToken);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(own.Id, loaded[0].Id);
            Assert.Equal("Hello team", loaded[0].Text);
            Assert.Equal(own.WrittenAt, loaded[0].WrittenAt);
            Assert.Equal(own.RecordedAt, loaded[0].RecordedAt);
            Assert.Equal(own.Envelope, loaded[0].Envelope);
            Assert.True(loaded[0].Envelope.TryOpen(key, out var opened));
            Assert.Equal("Hello team", opened.Text);
            Assert.Equal(StoredDeliveryState.Waiting, loaded[0].Deliveries[bob.IdentityKey]);
            Assert.Equal(StoredDeliveryState.Received, loaded[0].Deliveries[carol.IdentityKey]);
            Assert.Equal(bob.IdentityKey, loaded[1].AuthorKey);
            Assert.Empty(loaded[1].Deliveries);
        }

        Assert.Equal(3, (await store.Data.Messages.LoadAllAsync(cancellationToken)).Count);
    }

    [Fact]
    [Trait("Story", "US-12")]
    [Trait("NFR", "NFR-09")]
    public async Task GroupMessages_ASecondMessageWithAnAuthorsUsedSequence_IsNotStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            Assert.True(await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, bob, 5, "First", 1), cancellationToken));

            Assert.False(await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, bob, 5, "Second", 2), cancellationToken));
        }

        Assert.Single(await store.Data.GroupMessages.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-12")]
    public async Task GroupMessages_MarkReceivedByAnotherMemberOrInAnotherGroup_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            var own = GroupSamples.Message(record, key, user, 1, "Hello team", 3, bob.IdentityKey);
            Assert.True(await store.Data.GroupMessages.AddAsync(own, cancellationToken));

            Assert.False(await store.Data.GroupMessages.MarkReceivedAsync(record.GroupId, carol.IdentityKey, own.Id, cancellationToken));
            Assert.False(await store.Data.GroupMessages.MarkReceivedAsync(GroupId.Create(), bob.IdentityKey, own.Id, cancellationToken));
            Assert.True(await store.Data.GroupMessages.MarkReceivedAsync(record.GroupId, bob.IdentityKey, own.Id, cancellationToken));
            Assert.False(await store.Data.GroupMessages.MarkReceivedAsync(record.GroupId, bob.IdentityKey, own.Id, cancellationToken));
        }
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Group_StoredValues_HoldNoReadableNameKeyOrText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        var (record, key) = GroupSamples.Record(user, "Secret plans", (bob, "Bob"), (carol, "Carol"));
        var raw = new byte[GroupKey.Length];
        using (key)
        {
            key.Export(raw);
            await store.Data.Groups.AddAsync(GroupSamples.Group(record, key, user.IdentityKey), cancellationToken);
            await store.Data.GroupMessages.AddAsync(GroupSamples.Message(record, key, user, 1, "Meet at the bridge", 3, bob.IdentityKey, carol.IdentityKey), cancellationToken);
        }

        await store.CloseAsync();
        var bytes = await File.ReadAllBytesAsync(store.FilePath, cancellationToken);
        var wal = store.FilePath + "-wal";
        if (File.Exists(wal))
        {
            bytes = [.. bytes, .. await File.ReadAllBytesAsync(wal, cancellationToken)];
        }

        Assert.Equal(-1, bytes.AsSpan().IndexOf("Secret plans"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Meet at the bridge"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(raw));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(record.GroupId.Bytes));
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Group_KeySwappedIntoAnotherGroupsRow_IsDamaged()
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
        }

        await using (var connection = await store.RawAsync(cancellationToken))
        {
            await Sql.ExecuteAsync(connection, "UPDATE group_key SET key_enc = (SELECT key_enc FROM group_key WHERE group_id = 2) WHERE group_id = 1;", [], cancellationToken);
        }

        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));
    }
}
