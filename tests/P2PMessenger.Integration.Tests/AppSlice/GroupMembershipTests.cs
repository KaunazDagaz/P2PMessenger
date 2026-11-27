using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Integration.Tests.AppSlice;

public sealed class GroupMembershipTests
{
    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    public async Task LeaveGroup_BecomesReadOnlyReceivesNoNewMessagesAndSurvivesRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol);
            var groupId = await CreateThreeMemberGroupAsync(alice, bob, carol, cancellationToken);

            Assert.Equal(GroupSendOutcome.Queued, (await alice.Messenger.SendToGroupAsync(groupId, "Before bob left", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1 && carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

            Assert.Equal(LeaveGroupOutcome.Left, await bob.Messenger.LeaveGroupAsync(groupId, null, cancellationToken));
            Assert.Equal(GroupState.Left, StateAt(bob, groupId));
            Assert.Equal(GroupSendOutcome.Left, (await bob.Messenger.SendToGroupAsync(groupId, "Too late", cancellationToken)).Outcome);

            await Eventually.HoldsAsync(() =>
                alice.Messenger.Groups.TryGet(groupId, out var atAlice) && atAlice.Record.Epoch == 2 && atAlice.Find(Key(bob)) is null
                && carol.Messenger.Groups.TryGet(groupId, out var atCarol) && atCarol.Record.Epoch == 2 && atCarol.Find(Key(bob)) is null,
                cancellationToken);

            Assert.Equal(GroupSendOutcome.Queued, (await alice.Messenger.SendToGroupAsync(groupId, "After bob left", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 2, cancellationToken);

            await bob.DisposeAsync();
            bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
            Assert.Equal(GroupState.Left, StateAt(bob, groupId));
            var retained = Assert.Single(bob.Messenger.GroupChats.MessagesIn(groupId));
            Assert.Equal("Before bob left", retained.Text);

            await Eventually.HoldsAsync(() => alice.Messenger.Connections.IsConnected(Key(bob)), cancellationToken);
            await Task.Delay(400, cancellationToken);
            Assert.Single(bob.Messenger.GroupChats.MessagesIn(groupId));
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-13")]
    public async Task RemoveMember_OnlyAdminCanRemoveWithoutApprovalAndRemovedReceivesNothingFromNewEpoch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await AppPeer.StartAsync("Alice", cancellationToken);
        await using var bob = await AppPeer.StartAsync("Bob", cancellationToken);
        await using var carol = await AppPeer.StartAsync("Carol", cancellationToken);
        await AddEachOtherAsync(cancellationToken, alice, bob, carol);
        var groupId = await CreateThreeMemberGroupAsync(alice, bob, carol, cancellationToken);

        Assert.Equal(RemoveMemberOutcome.NotAuthorized, (await bob.Messenger.RemoveGroupMemberAsync(groupId, Key(carol), cancellationToken)).Outcome);

        Assert.True(carol.Messenger.Groups.TryGet(groupId, out var carolEpoch1));
        var epoch1KeyCheck = carolEpoch1.Record.KeyCheck.ToArray();

        Assert.Equal(GroupSendOutcome.Queued, (await alice.Messenger.SendToGroupAsync(groupId, "Epoch one secret", cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => carol.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);

        var removed = await alice.Messenger.RemoveGroupMemberAsync(groupId, Key(carol), cancellationToken);
        Assert.Equal(RemoveMemberOutcome.Removed, removed.Outcome);
        Assert.False(removed.Group!.Record.KeyCheck.SequenceEqual(epoch1KeyCheck));

        await Eventually.HoldsAsync(() =>
            StateAt(carol, groupId) == GroupState.Removed
            && bob.Messenger.Groups.TryGet(groupId, out var atBob) && atBob.Record.Epoch == 2 && atBob.Find(Key(carol)) is null,
            cancellationToken);

        Assert.Equal(GroupState.Removed, StateAt(carol, groupId));
        Assert.Equal(GroupSendOutcome.Removed, (await carol.Messenger.SendToGroupAsync(groupId, "Blocked", cancellationToken)).Outcome);

        Assert.Equal(GroupSendOutcome.Queued, (await bob.Messenger.SendToGroupAsync(groupId, "Epoch two secret", cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() => alice.Messenger.GroupChats.MessagesIn(groupId).Count == 2, cancellationToken);
        await Task.Delay(400, cancellationToken);
        Assert.Single(carol.Messenger.GroupChats.MessagesIn(groupId));
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    public async Task OfflineMember_LearnsNewEpochWhenReconnectingBeforeSending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol"), folder.DatabasePath("dave") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        var dave = await AppPeer.StartSavedAsync(paths[3], protectors[3], "Dave", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol, dave);
            var created = await alice.Messenger.CreateGroupAsync("Squad", [Key(bob), Key(carol), Key(dave)], cancellationToken);
            Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
            var groupId = created.Group!.Id;
            await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created && StateAt(dave, groupId) == GroupState.Created, cancellationToken);

            var bobKey = Key(bob);
            await bob.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(bobKey) && !dave.Messenger.Connections.IsConnected(bobKey), cancellationToken);

            Assert.Equal(RemoveMemberOutcome.Removed, (await alice.Messenger.RemoveGroupMemberAsync(groupId, Key(carol), cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => dave.Messenger.Groups.TryGet(groupId, out var atDave) && atDave.Record.Epoch == 2, cancellationToken);

            var aliceKey = Key(alice);
            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !dave.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
            await Eventually.HoldsAsync(() => bob.Messenger.Groups.TryGet(groupId, out var atBob) && atBob.Record.Epoch == 2 && atBob.Find(Key(carol)) is null, cancellationToken);

            var sent = await bob.Messenger.SendToGroupAsync(groupId, "On epoch two now", cancellationToken);
            Assert.Equal(GroupSendOutcome.Queued, sent.Outcome);
            await Eventually.HoldsAsync(() => dave.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
            Assert.Equal("On epoch two now", Assert.Single(dave.Messenger.GroupChats.MessagesIn(groupId)).Text);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
            await dave.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    public async Task AdminHandoverWhileMemberOffline_RelayedLeaveToOfflineAdmin_AndRestartPersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol);
            var groupId = await CreateThreeMemberGroupAsync(alice, bob, carol, cancellationToken);

            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !alice.Messenger.Connections.IsConnected(carolKey) && !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            Assert.Equal(LeaveGroupOutcome.Left, await alice.Messenger.LeaveGroupAsync(groupId, Key(bob), cancellationToken));
            await Eventually.HoldsAsync(() => bob.Messenger.Groups.TryGet(groupId, out var atBob) && atBob.IsAuthority && atBob.Record.Epoch == 2, cancellationToken);

            var aliceKey = Key(alice);
            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() =>
                carol.Messenger.Groups.TryGet(groupId, out var atCarol)
                && atCarol.Record.Epoch == 2
                && atCarol.AuthorityKey.Equals(Key(bob))
                && atCarol.State == GroupState.Created,
                cancellationToken);

            Assert.Equal(GroupSendOutcome.Queued, (await carol.Messenger.SendToGroupAsync(groupId, "After handover", cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => bob.Messenger.GroupChats.MessagesIn(groupId).Count == 1, cancellationToken);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    public async Task OfflineAdminReceivesRelayedLeave_AndRemovingUnaddedInviteeUnlocksGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol"), folder.DatabasePath("dave") };
        var alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var bob = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var carol = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        var dave = await AppPeer.StartSavedAsync(paths[3], protectors[3], "Dave", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, alice, bob, carol, dave);
            var created = await alice.Messenger.CreateGroupAsync("Relay", [Key(bob), Key(carol), Key(dave)], cancellationToken);
            Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
            var groupId = created.Group!.Id;
            await Eventually.HoldsAsync(() => StateAt(alice, groupId) == GroupState.Created && StateAt(bob, groupId) == GroupState.Created && StateAt(carol, groupId) == GroupState.Created && StateAt(dave, groupId) == GroupState.Created, cancellationToken);

            var aliceKey = Key(alice);
            await alice.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(aliceKey) && !carol.Messenger.Connections.IsConnected(aliceKey), cancellationToken);

            Assert.Equal(LeaveGroupOutcome.Left, await carol.Messenger.LeaveGroupAsync(groupId, null, cancellationToken));
            await Eventually.HoldsAsync(() => bob.Messenger.Groups.TryGet(groupId, out var atBob) && atBob.FindAny(Key(carol))!.State == GroupMemberState.Left, cancellationToken);

            var carolKey = Key(carol);
            await carol.DisposeAsync();
            await Eventually.HoldsAsync(() => !bob.Messenger.Connections.IsConnected(carolKey), cancellationToken);

            alice = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
            await Eventually.HoldsAsync(() =>
                alice.Messenger.Groups.TryGet(groupId, out var atAlice) && atAlice.Record.Epoch == 2
                && bob.Messenger.Groups.TryGet(groupId, out var atBob2) && atBob2.Record.Epoch == 2
                && dave.Messenger.Groups.TryGet(groupId, out var atDave2) && atDave2.Record.Epoch == 2,
                cancellationToken);
        }
        finally
        {
            await alice.DisposeAsync();
            await bob.DisposeAsync();
            await carol.DisposeAsync();
            await dave.DisposeAsync();
        }

        await using var owner = await AppPeer.StartAsync("Alice2", cancellationToken);
        await using var peer1 = await AppPeer.StartAsync("Bob2", cancellationToken);
        await using var peer2 = await AppPeer.StartAsync("Carol2", cancellationToken);
        await AddEachOtherAsync(cancellationToken, owner, peer1);
        await AddEachOtherAsync(cancellationToken, owner, peer2);
        var stuckId = (await owner.Messenger.CreateGroupAsync("Stuck", [Key(peer1), Key(peer2)], cancellationToken)).Group!.Id;
        await Eventually.HoldsAsync(() => StateAt(peer1, stuckId) == GroupState.Joining && StateAt(peer2, stuckId) == GroupState.Joining, cancellationToken);

        Assert.Equal(RemoveMemberOutcome.Removed, (await owner.Messenger.RemoveGroupMemberAsync(stuckId, Key(peer2), cancellationToken)).Outcome);
        await Eventually.HoldsAsync(() =>
            StateAt(owner, stuckId) == GroupState.Created
            && StateAt(peer1, stuckId) == GroupState.Created
            && StateAt(peer2, stuckId) == GroupState.Removed,
            cancellationToken);
    }

    [Fact(Timeout = 120_000)]
    [Trait("Story", "US-14")]
    public async Task RemovingOfflineInviteeWhoNeverReceivedInvitation_ClearsRemovalNoticeOnceTheyConnect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = new SavedFolder();
        var protectors = new[] { new TestKeyProtector(), new TestKeyProtector(), new TestKeyProtector() };
        var paths = new[] { folder.DatabasePath("alice"), folder.DatabasePath("bob"), folder.DatabasePath("carol") };
        var owner = await AppPeer.StartSavedAsync(paths[0], protectors[0], "Alice", cancellationToken);
        var peer1 = await AppPeer.StartSavedAsync(paths[1], protectors[1], "Bob", cancellationToken);
        var peer2 = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
        try
        {
            await AddEachOtherAsync(cancellationToken, owner, peer1, peer2);
            var peer2Key = Key(peer2);
            await peer2.DisposeAsync();
            await Eventually.HoldsAsync(() => !owner.Messenger.Connections.IsConnected(peer2Key), cancellationToken);

            var created = await owner.Messenger.CreateGroupAsync("NeverInvited", [Key(peer1), peer2Key], cancellationToken);
            Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
            var groupId = created.Group!.Id;
            await Eventually.HoldsAsync(() => StateAt(peer1, groupId) == GroupState.WaitingForMembers, cancellationToken);

            Assert.Equal(RemoveMemberOutcome.Removed, (await owner.Messenger.RemoveGroupMemberAsync(groupId, peer2Key, cancellationToken)).Outcome);
            await Eventually.HoldsAsync(() => StateAt(owner, groupId) == GroupState.Created && StateAt(peer1, groupId) == GroupState.Created, cancellationToken);
            Assert.True(owner.Messenger.Groups.TryGet(groupId, out var removedAtOwner));
            Assert.Equal(NoticeState.Waiting, removedAtOwner.FindAny(peer2Key)?.Removed);

            peer2 = await AppPeer.StartSavedAsync(paths[2], protectors[2], "Carol", cancellationToken);
            await Eventually.HoldsAsync(() =>
                owner.Messenger.Connections.IsConnected(peer2Key)
                && owner.Messenger.Groups.TryGet(groupId, out var updatedOwner)
                && updatedOwner.FindAny(peer2Key)?.Removed == NoticeState.Received,
                cancellationToken);

            Assert.False(peer2.Messenger.Groups.TryGet(groupId, out _));
        }
        finally
        {
            await owner.DisposeAsync();
            await peer1.DisposeAsync();
            await peer2.DisposeAsync();
        }
    }

    private static Ed25519PublicKey Key(AppPeer peer) => peer.Identity.Keys.IdentityKey;

    private static GroupState? StateAt(AppPeer peer, GroupId groupId) =>
        peer.Messenger.Groups.TryGet(groupId, out var group) ? group.State : null;

    private static async Task<GroupId> CreateThreeMemberGroupAsync(AppPeer alice, AppPeer bob, AppPeer carol, CancellationToken cancellationToken)
    {
        var created = await alice.Messenger.CreateGroupAsync("Field team", [Key(bob), Key(carol)], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        var groupId = created.Group!.Id;
        await Eventually.HoldsAsync(() =>
            StateAt(alice, groupId) == GroupState.Created
            && StateAt(bob, groupId) == GroupState.Created
            && StateAt(carol, groupId) == GroupState.Created,
            cancellationToken);
        return groupId;
    }

    private static async Task AddEachOtherAsync(CancellationToken cancellationToken, params AppPeer[] peers)
    {
        for (var first = 0; first < peers.Length; first++)
        {
            for (var second = first + 1; second < peers.Length; second++)
            {
                Assert.True((await peers[first].Messenger.Contacts.AddAsync(peers[first].Messenger.Contacts.Check(peers[second].Identity.CodeText), cancellationToken)).IsSaved);
                Assert.True((await peers[second].Messenger.Contacts.AddAsync(peers[second].Messenger.Contacts.Check(peers[first].Identity.CodeText), cancellationToken)).IsSaved);
            }
        }

        await Eventually.HoldsAsync(() =>
            peers.All(peer => peers.Where(other => other != peer).All(other =>
                peer.Messenger.Contacts.TryGet(Key(other), out var contact)
                && contact.IsAdded
                && peer.Messenger.Connections.IsConnected(Key(other)))),
            cancellationToken);
    }
}
