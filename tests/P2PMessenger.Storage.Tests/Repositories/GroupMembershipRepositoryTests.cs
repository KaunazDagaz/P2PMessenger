using P2PMessenger.Core.Groups;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Storage.Tests.Repositories;

public sealed class GroupMembershipRepositoryTests
{
    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-13")]
    public async Task AdvanceEpoch_RoundTripsHistoryAuthorityRemovedStateAndClearsLeaveProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var (record1, key1) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key1)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record1, key1, user.IdentityKey), cancellationToken);
        }

        var carolLeave = GroupLeave.Create(record1.GroupId, 1, carol);
        await store.Data.Groups.SaveMembershipAsync(
            record1.GroupId,
            user.IdentityKey,
            [new StoredGroupMember(carol.IdentityKey, StoredGroupMemberState.Left, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, 0)],
            carolLeave,
            cancellationToken);

        using var key2 = GroupKey.Create();
        var record2 = GroupRecord.Create(
            record1.GroupId,
            2,
            Samples.Name("Team"),
            key2,
            [
                new GroupMember(user.IdentityKey, Samples.Name("Alice")),
                new GroupMember(bob.IdentityKey, Samples.Name("Bob")),
            ],
            user);
        await store.Data.Groups.AdvanceEpochAsync(
            record1.GroupId,
            record2,
            key2,
            user.IdentityKey,
            [
                new StoredGroupMember(user.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, 0),
                new StoredGroupMember(bob.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, StoredNoticeState.None, StoredNoticeState.None, 0),
                new StoredGroupMember(carol.IdentityKey, StoredGroupMemberState.Removed, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, 2),
            ],
            [carol.IdentityKey],
            cancellationToken);

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = Assert.Single(await store.Data.Groups.LoadAllAsync(cancellationToken));
        using var loadedKey = loaded.Key;

        Assert.Equal(record2, loaded.Record);
        Assert.Equal([record1, record2], loaded.History);
        Assert.Equal(user.IdentityKey, loaded.AuthorityKey);
        Assert.Empty(loaded.LeaveProofs);
        Assert.True(loaded.Record.Fits(loadedKey));
        var removedCarol = loaded.Members.Single(member => member.IdentityKey.Equals(carol.IdentityKey));
        Assert.Equal(StoredGroupMemberState.Removed, removedCarol.State);
        Assert.Equal(StoredNoticeState.Waiting, removedCarol.Removed);
        Assert.Equal(2, removedCarol.RemovedEpoch);
        var activeBob = loaded.Members.Single(member => member.IdentityKey.Equals(bob.IdentityKey));
        Assert.Equal(StoredNoticeState.Waiting, activeBob.Update);
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task SaveMembership_HandoverLeaveProof_RoundTripsAndFailsOnTamperOrForgery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var (record1, key1) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key1)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record1, key1, user.IdentityKey), cancellationToken);
        }

        var handover = GroupLeave.Create(record1.GroupId, 1, user, bob.IdentityKey);
        await store.Data.Groups.SaveMembershipAsync(
            record1.GroupId,
            bob.IdentityKey,
            [
                new StoredGroupMember(user.IdentityKey, StoredGroupMemberState.Left, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, 0),
                new StoredGroupMember(bob.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, StoredNoticeState.None, 0),
                new StoredGroupMember(carol.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, StoredNoticeState.None, 0),
            ],
            handover,
            cancellationToken);

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var loaded = Assert.Single(await store.Data.Groups.LoadAllAsync(cancellationToken));
        using var loadedKey = loaded.Key;
        Assert.Equal(bob.IdentityKey, loaded.AuthorityKey);
        Assert.Equal(handover, Assert.Single(loaded.LeaveProofs));

        await using var raw = await store.RawAsync(cancellationToken);
        var originalProof = await ReadBytesAsync(raw, "SELECT proof_enc FROM group_leave_proof LIMIT 1;", cancellationToken);
        var tamperedProof = (byte[])originalProof.Clone();
        tamperedProof[0] ^= 1;
        await Sql.ExecuteAsync(raw, "UPDATE group_leave_proof SET proof_enc = $proof;", [("$proof", tamperedProof)], cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));

        await Sql.ExecuteAsync(raw, "UPDATE group_leave_proof SET proof_enc = $proof;", [("$proof", originalProof)], cancellationToken);
        var originalAuthority = await ReadBytesAsync(raw, "SELECT authority_enc FROM chat_group WHERE id = 1;", cancellationToken);
        var tamperedAuthority = (byte[])originalAuthority.Clone();
        tamperedAuthority[0] ^= 1;
        await Sql.ExecuteAsync(raw, "UPDATE chat_group SET authority_enc = $auth WHERE id = 1;", [("$auth", tamperedAuthority)], cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-11")]
    public async Task LoadAll_ResealedForgedLeaveProofOrRemovedNoticeOnNonRemovedMember_IsDamaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var (record1, key1) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key1)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record1, key1, user.IdentityKey), cancellationToken);
        }

        await using var raw = await store.RawAsync(cancellationToken);
        await Sql.ExecuteAsync(raw, "UPDATE group_member SET removed_epoch = 2 WHERE person_id = 2;", [], cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));

        await Sql.ExecuteAsync(raw, "UPDATE group_member SET removed_epoch = 0, leave_state = 1 WHERE person_id = 2;", [], cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));

        await Sql.ExecuteAsync(raw, "UPDATE group_member SET leave_state = 0;", [], cancellationToken);
        var badLeave = GroupLeave.Create(record1.GroupId, 1, bob).ToBytes();
        badLeave[^1] ^= 1;
        var keys = P2PMessenger.Storage.Protection.RecordKeys.Derive(Samples.DataKey());
        var sealedBadProof = keys.Seal(Locations.GroupLeaveProof(1, 2), badLeave);
        await Sql.ExecuteAsync(
            raw,
            "INSERT INTO group_leave_proof (group_id, member_person_id, proof_enc) VALUES (1, 2, $proof);",
            [("$proof", sealedBadProof)],
            cancellationToken);
        await Assert.ThrowsAsync<StoredDataException>(() => store.Data.Groups.LoadAllAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-11")]
    public async Task StoredGroup_HistoryAndMessagesFromRemovedMember_HoldNoPlaintextAndLoadAfterRemoval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = await StorageFixture.OpenAsync(cancellationToken);
        var (user, bob, carol) = await store.FillAsync(cancellationToken);
        using var userKeys = user;
        using var bobKeys = bob;
        using var carolKeys = carol;
        var (record1, key1) = GroupSamples.Record(user, "Team", (bob, "Bob"), (carol, "Carol"));
        using (key1)
        {
            await store.Data.Groups.AddAsync(GroupSamples.Group(record1, key1, user.IdentityKey), cancellationToken);
            var msgFromCarol = GroupSamples.Message(record1, key1, carol, 1, "Before leaving", 1);
            Assert.True(await store.Data.GroupMessages.AddAsync(msgFromCarol, cancellationToken));
        }

        using var key2 = GroupKey.Create();
        var record2 = GroupRecord.Create(
            record1.GroupId,
            2,
            Samples.Name("Team"),
            key2,
            [
                new GroupMember(user.IdentityKey, Samples.Name("Alice")),
                new GroupMember(bob.IdentityKey, Samples.Name("Bob")),
            ],
            user);
        await store.Data.Groups.AdvanceEpochAsync(
            record1.GroupId,
            record2,
            key2,
            user.IdentityKey,
            [
                new StoredGroupMember(user.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, 0),
                new StoredGroupMember(bob.IdentityKey, StoredGroupMemberState.Member, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, StoredNoticeState.None, StoredNoticeState.None, 0),
                new StoredGroupMember(carol.IdentityKey, StoredGroupMemberState.Removed, StoredMemberJoin.Joined, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.Waiting, 2),
            ],
            [],
            cancellationToken);

        await store.ReopenAsync(Samples.DataKey(), cancellationToken);
        var groups = await store.Data.Groups.LoadAllAsync(cancellationToken);
        var messages = await store.Data.GroupMessages.LoadAllAsync(cancellationToken);
        groups[0].Key.Dispose();

        Assert.Single(groups);
        Assert.Single(messages);
        Assert.Equal("Before leaving", messages[0].Text);

        await store.CloseAsync();
        var bytes = await File.ReadAllBytesAsync(store.FilePath, cancellationToken);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Before leaving"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(user.IdentityKey.Bytes));
    }

    private static async Task<byte[]> ReadBytesAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (byte[])(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
    }
}
