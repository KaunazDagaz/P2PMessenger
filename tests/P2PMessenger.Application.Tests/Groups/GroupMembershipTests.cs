using System.Net;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Tests.Shared;
using static P2PMessenger.Application.Tests.Groups.TestGroups;
using static P2PMessenger.Application.Tests.TestKeys;

namespace P2PMessenger.Application.Tests.Groups;

public sealed class GroupMembershipTests
{
    [Fact]
    [Trait("Story", "US-14")]
    [Trait("NFR", "NFR-13")]
    public async Task RemoveMember_ByAdminInCreatedGroup_RotatesKeyAndEpochAndStopsDeliveries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        var record = await CreateThreeMemberAsync(alice, bob, "Bob", carol, "Carol", cancellationToken);
        Assert.True(alice.Groups.TryGetKey(record.GroupId, out _, out var epoch1Key));
        var epoch1Bytes = new byte[GroupKey.Length];
        epoch1Key!.Export(epoch1Bytes);
        using var epoch1Copy = GroupKey.TryImport(epoch1Bytes, out var imported1) ? imported1 : throw new InvalidOperationException();

        var sent1 = (await alice.SendToGroupAsync(record.GroupId, "epoch one", cancellationToken)).Message!;
        Assert.Contains(alice.GroupChats.WaitingFor(carol.IdentityKey), item => item.Id.Equals(sent1.Id));

        var removed = await alice.RemoveGroupMemberAsync(record.GroupId, carol.IdentityKey, cancellationToken);

        Assert.Equal(RemoveMemberOutcome.Removed, removed.Outcome);
        Assert.True(removed.Succeeded);
        Assert.Equal(GroupState.Created, removed.Group.State);
        Assert.Equal(2, removed.Group.Record.Epoch);
        Assert.True(removed.Group.IsAuthority);
        Assert.Equal(alice.Identity.Keys.IdentityKey, removed.Group.AuthorityKey);
        Assert.Equal(2, removed.Group.Members.Count);
        Assert.Null(removed.Group.Find(carol.IdentityKey));
        var storedCarol = removed.Group.FindAny(carol.IdentityKey);
        Assert.NotNull(storedCarol);
        Assert.Equal(GroupMemberState.Removed, storedCarol.State);
        Assert.Equal(2, storedCarol.RemovedEpoch);
        Assert.Equal(NoticeState.Waiting, storedCarol.Removed);
        Assert.Equal(NoticeState.Waiting, removed.Group.Find(bob.IdentityKey)!.Update);

        Assert.True(alice.Groups.TryGetKey(record.GroupId, out _, out var epoch2Key));
        var epoch2Bytes = new byte[GroupKey.Length];
        epoch2Key!.Export(epoch2Bytes);
        Assert.False(epoch1Bytes.AsSpan().SequenceEqual(epoch2Bytes));

        Assert.Empty(alice.GroupChats.WaitingFor(carol.IdentityKey));
        var carolPending = Assert.Single(alice.Groups.PendingNoticesFor(carol.IdentityKey));
        var carolRemovedNotice = Assert.IsType<GroupRemoved>(carolPending.Message);
        Assert.Equal(2, carolRemovedNotice.NewEpoch);
        Assert.Equal(carol.IdentityKey, carolRemovedNotice.RemovedMemberKey);

        var bobPending = Assert.Single(alice.Groups.PendingNoticesFor(bob.IdentityKey));
        var bobUpdate = Assert.IsType<GroupUpdate>(bobPending.Message);
        Assert.Equal(2, bobUpdate.Record.Epoch);
        Assert.False(bobUpdate.Record.HasMember(carol.IdentityKey));

        var ackedRemoved = await alice.Groups.MarkNoticeReceivedAsync(carol.IdentityKey, carolRemovedNotice.NoticeId, cancellationToken);
        Assert.True(ackedRemoved);
        Assert.Empty(alice.Groups.PendingNoticesFor(carol.IdentityKey));
        Assert.Equal(NoticeState.Received, alice.Groups.TryGet(record.GroupId, out var afterAck) ? afterAck.FindAny(carol.IdentityKey)!.Removed : NoticeState.None);

        var sent2 = (await alice.SendToGroupAsync(record.GroupId, "epoch two", cancellationToken)).Message!;
        Assert.Empty(alice.GroupChats.WaitingFor(carol.IdentityKey));
        Assert.Single(alice.GroupChats.WaitingFor(bob.IdentityKey));
        var waitingEnvelope = Assert.IsType<GroupMessage>(Assert.Single(alice.GroupChats.WaitingFor(bob.IdentityKey)).Message);
        Assert.False(waitingEnvelope.TryOpen(epoch1Copy, out _));
        Assert.True(waitingEnvelope.TryOpen(epoch2Key, out var opened2));
        Assert.Equal(sent2.Id, opened2.Id);
        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task RemoveMember_InJoiningOrWaitingOrTwoMemberGroup_HandlesAuthorityAndConstraints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        await AddContactAsync(alice, bob, "Bob", cancellationToken);
        await AddContactAsync(alice, carol, "Carol", cancellationToken);

        var waiting = (await alice.CreateGroupAsync("Hikers", [bob.IdentityKey, carol.IdentityKey], cancellationToken)).Group!;
        Assert.Equal(GroupState.WaitingForMembers, waiting.State);
        Assert.Equal(RemoveMemberOutcome.CannotRemoveSelf, (await alice.RemoveGroupMemberAsync(waiting.Id, alice.Identity.Keys.IdentityKey, cancellationToken)).Outcome);
        Assert.Equal(RemoveMemberOutcome.NotAMember, (await alice.RemoveGroupMemberAsync(waiting.Id, KeySet.Create().IdentityKey, cancellationToken)).Outcome);
        Assert.Equal(RemoveMemberOutcome.NotAGroup, (await alice.RemoveGroupMemberAsync(GroupId.Create(), bob.IdentityKey, cancellationToken)).Outcome);

        var unlockJoin = await alice.RemoveGroupMemberAsync(waiting.Id, carol.IdentityKey, cancellationToken);
        Assert.Equal(RemoveMemberOutcome.Removed, unlockJoin.Outcome);
        Assert.Equal(GroupState.WaitingForMembers, unlockJoin.Group!.State);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bob.IdentityKey, GroupJoin.Create(unlockJoin.Group.Record), cancellationToken));
        Assert.True(alice.Groups.TryGet(waiting.Id, out var nowCreated));
        Assert.Equal(GroupState.Created, nowCreated.State);

        var ended = await alice.RemoveGroupMemberAsync(waiting.Id, bob.IdentityKey, cancellationToken);
        Assert.Equal(RemoveMemberOutcome.EndedGroup, ended.Outcome);
        Assert.Equal(GroupState.Ended, ended.Group!.State);
        Assert.Equal(GroupSendOutcome.Ended, (await alice.SendToGroupAsync(waiting.Id, "after end", cancellationToken)).Outcome);
        Assert.Equal(RemoveMemberOutcome.NotActive, (await alice.RemoveGroupMemberAsync(waiting.Id, bob.IdentityKey, cancellationToken)).Outcome);

        var endedPending = Assert.Single(alice.Groups.PendingNoticesFor(bob.IdentityKey));
        Assert.IsType<GroupRemoved>(endedPending.Message);

        await using var bobApp = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var key = GroupKey.Create();
        using var outsider = KeySet.Create();
        await AddContactAsync(bobApp, outsider, "Alice", cancellationToken);
        await AddContactAsync(bobApp, carol, "Carol", cancellationToken);
        GroupMember[] three = [new(bobApp.Identity.Keys.IdentityKey, bobApp.Identity.Name), new(outsider.IdentityKey, Name("Alice")), new(carol.IdentityKey, Name("Carol"))];
        var invited = GroupRecord.Create(GroupId.Create(), 1, Name("Climb"), key, three, outsider);
        Assert.True(await bobApp.Groups.ReceiveInvitationAsync(outsider.IdentityKey, GroupInvitation.Create(key, invited), cancellationToken));
        Assert.True(await bobApp.Groups.ReceiveJoinAsync(carol.IdentityKey, GroupJoin.Create(invited), cancellationToken));
        Assert.Equal(RemoveMemberOutcome.NotAuthorized, (await bobApp.RemoveGroupMemberAsync(invited.GroupId, carol.IdentityKey, cancellationToken)).Outcome);

        using var forgedKey = GroupKey.Create();
        GroupMember[] forgedMembers = [three[0], three[2]];
        var forgedRecord = GroupRecord.Create(invited.GroupId, 2, invited.Name, forgedKey, forgedMembers, carol);
        Assert.False(await bobApp.Groups.ReceiveUpdateAsync(carol.IdentityKey, GroupUpdate.Create(forgedKey, forgedRecord), cancellationToken));
        Assert.False(await bobApp.Groups.ReceiveRemovedAsync(carol.IdentityKey, GroupRemoved.Create(invited.GroupId, 2, bobApp.Identity.Keys.IdentityKey, carol), cancellationToken));

        using var joiningKey = GroupKey.Create();
        await using var dave = await TestMessengers.StartAsync("Dave", cancellationToken);
        using var missing = KeySet.Create();
        await AddContactAsync(dave, outsider, "Admin", cancellationToken);
        GroupMember[] joiningMembers = [new(dave.Identity.Keys.IdentityKey, dave.Identity.Name), new(outsider.IdentityKey, Name("Admin")), new(missing.IdentityKey, Name("Missing"))];
        var joiningRecord = GroupRecord.Create(GroupId.Create(), 1, Name("Rope"), joiningKey, joiningMembers, outsider);
        Assert.True(await dave.Groups.ReceiveInvitationAsync(outsider.IdentityKey, GroupInvitation.Create(joiningKey, joiningRecord), cancellationToken));
        Assert.Equal(GroupState.Joining, dave.Groups.TryGet(joiningRecord.GroupId, out var joiningGroup) ? joiningGroup.State : GroupState.Created);

        using var epoch2Key = GroupKey.Create();
        var shrunkRecord = GroupRecord.Create(joiningRecord.GroupId, 2, joiningRecord.Name, epoch2Key, joiningMembers[..2], outsider);
        Assert.True(await dave.Groups.ReceiveUpdateAsync(outsider.IdentityKey, GroupUpdate.Create(epoch2Key, shrunkRecord), cancellationToken));
        Assert.Equal(GroupState.Created, dave.Groups.TryGet(joiningRecord.GroupId, out var joinedGroup) ? joinedGroup.State : GroupState.Joining);

        alice.Identity.Dispose();
        bobApp.Identity.Dispose();
        dave.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task LeaveGroup_NonAdminAndAdminHandoverAndMultiHopProofChain_PreserveAuthorityAndRotateKeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        using var dave = KeySet.Create();
        var record = await CreateFourMemberAsync(alice, bob, "Bob", carol, "Carol", dave, "Dave", cancellationToken);

        Assert.Equal(LeaveGroupOutcome.SuccessorRequired, await alice.LeaveGroupAsync(record.GroupId, null, cancellationToken));
        Assert.Equal(LeaveGroupOutcome.InvalidSuccessor, await alice.LeaveGroupAsync(record.GroupId, alice.Identity.Keys.IdentityKey, cancellationToken));
        Assert.Equal(LeaveGroupOutcome.InvalidSuccessor, await alice.LeaveGroupAsync(record.GroupId, KeySet.Create().IdentityKey, cancellationToken));

        Assert.Equal(LeaveGroupOutcome.Left, await alice.LeaveGroupAsync(record.GroupId, bob.IdentityKey, cancellationToken));
        Assert.True(alice.Groups.TryGet(record.GroupId, out var atAliceAfterLeave));
        Assert.Equal(GroupState.Left, atAliceAfterLeave.State);
        Assert.Equal(GroupSendOutcome.Left, (await alice.SendToGroupAsync(record.GroupId, "gone", cancellationToken)).Outcome);
        Assert.Equal(LeaveGroupOutcome.NotActive, await alice.LeaveGroupAsync(record.GroupId, carol.IdentityKey, cancellationToken));

        var bobNotice = Assert.IsType<GroupLeave>(Assert.Single(alice.Groups.PendingNoticesFor(bob.IdentityKey)).Message);
        var carolNotice = Assert.IsType<GroupLeave>(Assert.Single(alice.Groups.PendingNoticesFor(carol.IdentityKey)).Message);
        Assert.Equal(bobNotice, carolNotice);
        Assert.True(bobNotice.HasSuccessor);
        Assert.Equal(bob.IdentityKey, bobNotice.SuccessorKey);

        await using var bobApp = await TestMessengers.StartAsync("Bob", cancellationToken);
        var daveApp = await TestMessengers.StartAsync("Dave", cancellationToken);
        try
        {
            using var aliceKey = KeySet.Create();
            using var carolKey = KeySet.Create();
            using var initialKey = GroupKey.Create();
            await AddContactAsync(bobApp, aliceKey, "Alice", cancellationToken);
            await AddContactAsync(bobApp, carolKey, "Carol", cancellationToken);
            await AddContactAsync(bobApp, daveApp.Identity.Keys, "Dave", cancellationToken);
            await AddContactAsync(daveApp, aliceKey, "Alice", cancellationToken);
            await AddContactAsync(daveApp, bobApp.Identity.Keys, "Bob", cancellationToken);
            await AddContactAsync(daveApp, carolKey, "Carol", cancellationToken);

            GroupMember[] four =
            [
                new(aliceKey.IdentityKey, Name("Alice")),
                new(bobApp.Identity.Keys.IdentityKey, bobApp.Identity.Name),
                new(carolKey.IdentityKey, Name("Carol")),
                new(daveApp.Identity.Keys.IdentityKey, daveApp.Identity.Name),
            ];
            var hopRecord = GroupRecord.Create(GroupId.Create(), 1, Name("Relay"), initialKey, four, aliceKey);
            var invite = GroupInvitation.Create(initialKey, hopRecord);
            Assert.True(await bobApp.Groups.ReceiveInvitationAsync(aliceKey.IdentityKey, invite, cancellationToken));
            Assert.True(await bobApp.Groups.ReceiveJoinAsync(carolKey.IdentityKey, GroupJoin.Create(hopRecord), cancellationToken));
            Assert.True(await bobApp.Groups.ReceiveJoinAsync(daveApp.Identity.Keys.IdentityKey, GroupJoin.Create(hopRecord), cancellationToken));

            Assert.True(await daveApp.Groups.ReceiveInvitationAsync(aliceKey.IdentityKey, invite, cancellationToken));
            Assert.True(await daveApp.Groups.ReceiveJoinAsync(bobApp.Identity.Keys.IdentityKey, GroupJoin.Create(hopRecord), cancellationToken));
            Assert.True(await daveApp.Groups.ReceiveJoinAsync(carolKey.IdentityKey, GroupJoin.Create(hopRecord), cancellationToken));

            var aliceHandover = GroupLeave.Create(hopRecord.GroupId, 1, aliceKey, bobApp.Identity.Keys.IdentityKey);
            Assert.True(await bobApp.Groups.ReceiveLeaveAsync(aliceKey.IdentityKey, aliceHandover, cancellationToken));
            Assert.True(bobApp.Groups.TryGet(hopRecord.GroupId, out var bobEpoch2));
            Assert.True(bobEpoch2.IsAuthority);
            Assert.Equal(2, bobEpoch2.Record.Epoch);

            Assert.Equal(LeaveGroupOutcome.Left, await bobApp.LeaveGroupAsync(hopRecord.GroupId, carolKey.IdentityKey, cancellationToken));
            var bobHandover = Assert.IsType<GroupLeave>(Assert.Single(bobApp.Groups.PendingNoticesFor(carolKey.IdentityKey)).Message);

            Assert.False(await daveApp.Groups.ReceiveLeaveAsync(carolKey.IdentityKey, bobHandover, cancellationToken));
            Assert.True(await daveApp.Groups.ReceiveLeaveAsync(bobApp.Identity.Keys.IdentityKey, aliceHandover, cancellationToken));
            Assert.True(await daveApp.Groups.ReceiveLeaveAsync(carolKey.IdentityKey, bobHandover, cancellationToken));

            using var epoch3Key = GroupKey.Create();
            var epoch3Record = GroupRecord.Create(
                hopRecord.GroupId,
                3,
                hopRecord.Name,
                epoch3Key,
                [new(carolKey.IdentityKey, Name("Carol")), new(daveApp.Identity.Keys.IdentityKey, daveApp.Identity.Name)],
                carolKey);
            Assert.True(await daveApp.Groups.ReceiveUpdateAsync(carolKey.IdentityKey, GroupUpdate.Create(epoch3Key, epoch3Record), cancellationToken));
            Assert.True(daveApp.Groups.TryGet(hopRecord.GroupId, out var daveEpoch3));
            Assert.Equal(3, daveEpoch3.Record.Epoch);
            Assert.Equal(carolKey.IdentityKey, daveEpoch3.AuthorityKey);
            Assert.Equal(GroupState.Created, daveEpoch3.State);

            Assert.True(await daveApp.Groups.ReceiveRemovedAsync(carolKey.IdentityKey, GroupRemoved.Create(hopRecord.GroupId, 4, daveApp.Identity.Keys.IdentityKey, carolKey), cancellationToken));
            Assert.Equal(GroupState.Removed, daveApp.Groups.TryGet(hopRecord.GroupId, out var daveRemoved) ? daveRemoved.State : GroupState.Created);
            Assert.Equal(GroupSendOutcome.Removed, (await daveApp.SendToGroupAsync(hopRecord.GroupId, "removed", cancellationToken)).Outcome);
            Assert.Null(await daveApp.GroupChats.ReceiveAsync(carolKey.IdentityKey, From(carolKey, epoch3Record, epoch3Key, 1, "ignored"), cancellationToken));
        }
        finally
        {
            await daveApp.DisposeAsync();
        }

        alice.Identity.Dispose();
        bobApp.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task NonAdminLeaveAndTwoMemberAdminLeave_AndPcCopyCandidates_HandleAllEndStates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        using var bob = KeySet.Create();
        var record = await CreateTwoMemberAsync(alice, bob, "Bob", cancellationToken);

        Assert.Equal(GroupSendOutcome.Queued, (await alice.SendToGroupAsync(record.GroupId, "Before leave", cancellationToken)).Outcome);
        var fromBob = From(bob, record, (await AliceKeyAsync(alice, record.GroupId))!, 1, "Bye Alice");
        Assert.NotNull(await alice.GroupChats.ReceiveAsync(bob.IdentityKey, fromBob, cancellationToken));

        var bobLeave = GroupLeave.Create(record.GroupId, 1, bob, null);
        Assert.True(await alice.Groups.ReceiveLeaveAsync(bob.IdentityKey, bobLeave, cancellationToken));
        Assert.True(alice.Groups.TryGet(record.GroupId, out var aliceEnded));
        Assert.Equal(GroupState.Ended, aliceEnded.State);
        Assert.Equal(GroupMemberState.Left, aliceEnded.FindAny(bob.IdentityKey)!.State);

        var candidate = Assert.Single(alice.PcCopy.ListCandidates(), static item => item.IsGroup);
        Assert.Equal(2, candidate.TotalMessageCount);

        await using var emptyEnded = await TestMessengers.StartAsync("Carol", cancellationToken);
        using var dave = KeySet.Create();
        var emptyRecord = await CreateTwoMemberAsync(emptyEnded, dave, "Dave", cancellationToken);
        Assert.Equal(LeaveGroupOutcome.Left, await emptyEnded.LeaveGroupAsync(emptyRecord.GroupId, null, cancellationToken));
        Assert.DoesNotContain(emptyEnded.PcCopy.ListCandidates(), static item => item.IsGroup);

        using var friend = KeySet.Create();
        var hub = new LoopbackDiscovery();
        await using var phone = await StartCopyPeerAsync("Phone", hub.CreateNode(), cancellationToken);
        await using var tablet = await StartCopyPeerAsync("Tablet", hub.CreateNode(), cancellationToken);
        var r2 = await CreateTwoMemberAsync(phone, friend, "Bob", cancellationToken);
        var k2 = (await AliceKeyAsync(phone, r2.GroupId))!;
        Assert.NotNull(await phone.GroupChats.ReceiveAsync(friend.IdentityKey, From(friend, r2, k2, 1, "Former speaker"), cancellationToken));
        Assert.Equal(RemoveMemberOutcome.EndedGroup, (await phone.RemoveGroupMemberAsync(r2.GroupId, friend.IdentityKey, cancellationToken)).Outcome);
        var copyCandidate = Assert.Single(phone.PcCopy.ListCandidates(), static item => item.IsGroup);

        var started = await tablet.PcCopy.StartReceivingAsync(cancellationToken);
        Assert.True(started.Started);
        await using (var receiver = started.Receiver)
        {
            var receiveTask = receiver.RunAsync(null, cancellationToken);
            var connected = await phone.PcCopy.ConnectAsync(receiver.PairingCode, cancellationToken);
            Assert.True(connected.Connected);
            await using var sender = connected.Sender;
            Assert.Equal(PcCopySendOutcome.Completed, (await sender.SendAsync([copyCandidate.Origin], null, cancellationToken)).Outcome);
            Assert.Equal(PcCopyReceivePhase.Completed, (await receiveTask).Outcome);
        }

        Assert.True(tablet.CopiedChats.TryGet(copyCandidate.Origin, out var copied));
        var copiedMsg = Assert.Single(copied!.Messages);
        Assert.Equal("Former speaker", copiedMsg.Text);
        Assert.Equal("Bob", copiedMsg.AuthorName?.Value);

        alice.Identity.Dispose();
        emptyEnded.Identity.Dispose();
        phone.Identity.Dispose();
        tablet.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task SendRacingWithMembershipRotationAndLeave_SerializesEpochAndNeverSticksOrUsesDisposedKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seen = new InMemorySeenMessageIds();
        using var innerStore = new InMemoryGroupStore(seen);
        var pausingStore = new PausingGroupStore(innerStore);
        await using var alice = await Messenger.StartAsync(
            NewIdentity("Alice"),
            MessengerSetup.InMemory() with
            {
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                Connection = TestMessengers.Fast,
                GroupStore = pausingStore,
                SeenMessageIds = seen,
            },
            cancellationToken);
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();
        var record = await CreateThreeMemberAsync(alice, bob, "Bob", carol, "Carol", cancellationToken);

        var advanceEntered = pausingStore.ArmAdvanceEpoch();
        var removeTask = alice.RemoveGroupMemberAsync(record.GroupId, carol.IdentityKey, cancellationToken);
        await advanceEntered.WaitAsync(cancellationToken);

        var sendTask = alice.SendToGroupAsync(record.GroupId, "after rotation", cancellationToken);
        await Task.Yield();
        Assert.False(sendTask.IsCompleted);

        pausingStore.ReleaseAdvanceEpoch();
        var removed = await removeTask;
        var sent = await sendTask;

        Assert.Equal(RemoveMemberOutcome.Removed, removed.Outcome);
        Assert.Equal(GroupSendOutcome.Queued, sent.Outcome);
        Assert.NotNull(sent.Message);
        Assert.Equal(2, sent.Message.Envelope.Epoch);
        Assert.Equal([bob.IdentityKey], sent.Message.Deliveries.Keys.ToArray());
        Assert.Contains(alice.GroupChats.WaitingFor(bob.IdentityKey), item => item.Id.Equals(sent.Message.Id));
        Assert.Empty(alice.GroupChats.WaitingFor(carol.IdentityKey));

        var saveEntered = pausingStore.ArmSaveMembership();
        var leaveTask = alice.LeaveGroupAsync(record.GroupId, null, cancellationToken);
        await saveEntered.WaitAsync(cancellationToken);

        var sendWhileLeavingTask = alice.SendToGroupAsync(record.GroupId, "too late", cancellationToken);
        await Task.Yield();
        Assert.False(sendWhileLeavingTask.IsCompleted);

        pausingStore.ReleaseSaveMembership();
        Assert.Equal(LeaveGroupOutcome.Left, await leaveTask);
        var sendWhileLeaving = await sendWhileLeavingTask;
        Assert.Equal(GroupSendOutcome.Left, sendWhileLeaving.Outcome);
        Assert.Null(sendWhileLeaving.Message);
        Assert.Single(alice.GroupChats.MessagesIn(record.GroupId));

        alice.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task UnknownGroup_RejectsGroupUpdate_AndAcknowledgesValidDirectGroupRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var bobApp = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var outsider = KeySet.Create();
        using var dave = KeySet.Create();
        await AddContactAsync(bobApp, outsider, "Alice", cancellationToken);

        var unknownId = GroupId.Create();
        using var updateKey = GroupKey.Create();
        GroupMember[] members =
        [
            new(bobApp.Identity.Keys.IdentityKey, bobApp.Identity.Name),
            new(outsider.IdentityKey, Name("Alice")),
        ];
        var epoch2Record = GroupRecord.Create(unknownId, 2, Name("Spoofed"), updateKey, members, outsider);

        Assert.False(await bobApp.Groups.ReceiveUpdateAsync(outsider.IdentityKey, GroupUpdate.Create(updateKey, epoch2Record), cancellationToken));
        Assert.False(bobApp.Groups.TryGet(unknownId, out _));

        using var otherKey = GroupKey.Create();
        var omitSelfRecord = GroupRecord.Create(
            unknownId,
            2,
            Name("Other"),
            otherKey,
            [new(outsider.IdentityKey, Name("Alice")), new(dave.IdentityKey, Name("Dave"))],
            outsider);
        Assert.False(await bobApp.Groups.ReceiveUpdateAsync(outsider.IdentityKey, GroupUpdate.Create(otherKey, omitSelfRecord), cancellationToken));

        var validDirectRemoved = GroupRemoved.Create(unknownId, 2, bobApp.Identity.Keys.IdentityKey, outsider);
        Assert.True(await bobApp.Groups.ReceiveRemovedAsync(outsider.IdentityKey, validDirectRemoved, cancellationToken));
        Assert.False(bobApp.Groups.TryGet(unknownId, out _));

        Assert.False(await bobApp.Groups.ReceiveRemovedAsync(dave.IdentityKey, validDirectRemoved, cancellationToken));
        Assert.False(await bobApp.Groups.ReceiveRemovedAsync(outsider.IdentityKey, GroupRemoved.Create(unknownId, 2, dave.IdentityKey, outsider), cancellationToken));

        bobApp.Identity.Dispose();
    }

    [Fact]
    [Trait("Story", "US-14")]
    public async Task RemoveMemberBeforePeerJoinArrives_UpdateAndStaleEpochJoinConvergeToCreated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var alice = await TestMessengers.StartAsync("Alice", cancellationToken);
        await using var bobApp = await TestMessengers.StartAsync("Bob", cancellationToken);
        using var carol = KeySet.Create();

        await AddContactAsync(alice, bobApp.Identity.Keys, "Bob", cancellationToken);
        await AddContactAsync(alice, carol, "Carol", cancellationToken);
        await AddContactAsync(bobApp, alice.Identity.Keys, "Alice", cancellationToken);
        await AddContactAsync(bobApp, carol, "Carol", cancellationToken);

        var created1 = await alice.CreateGroupAsync("RaceOne", [bobApp.Identity.Keys.IdentityKey, carol.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created1.Outcome);
        var record1 = created1.Group!.Record;
        var invite1 = Assert.IsType<GroupInvitation>(Assert.Single(alice.Groups.PendingNoticesFor(bobApp.Identity.Keys.IdentityKey), item => item.Message is GroupInvitation invite && invite.Record.GroupId.Equals(record1.GroupId)).Message);

        Assert.True(await bobApp.Groups.ReceiveInvitationAsync(alice.Identity.Keys.IdentityKey, invite1, cancellationToken));
        Assert.True(bobApp.Groups.TryGet(record1.GroupId, out var bobGroup1));
        Assert.Equal(GroupState.WaitingForMembers, bobGroup1.State);

        var removed1 = await alice.RemoveGroupMemberAsync(record1.GroupId, carol.IdentityKey, cancellationToken);
        Assert.Equal(RemoveMemberOutcome.Removed, removed1.Outcome);
        Assert.Equal(GroupState.WaitingForMembers, removed1.Group!.State);
        Assert.Equal(2, removed1.Group.Record.Epoch);

        var update1 = Assert.IsType<GroupUpdate>(Assert.Single(alice.Groups.PendingNoticesFor(bobApp.Identity.Keys.IdentityKey), item => item.Message is GroupUpdate update && update.Record.GroupId.Equals(record1.GroupId)).Message);
        Assert.True(await bobApp.Groups.ReceiveUpdateAsync(alice.Identity.Keys.IdentityKey, update1, cancellationToken));
        Assert.True(bobApp.Groups.TryGet(record1.GroupId, out var bobUpdated1));
        Assert.Equal(GroupState.Created, bobUpdated1.State);
        Assert.Equal(2, bobUpdated1.Record.Epoch);

        var bobJoin2 = Assert.IsType<GroupJoin>(Assert.Single(bobApp.Groups.PendingNoticesFor(alice.Identity.Keys.IdentityKey), item => item.Message is GroupJoin join && join.GroupId.Equals(record1.GroupId)).Message);
        Assert.Equal(2, bobJoin2.Epoch);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bobApp.Identity.Keys.IdentityKey, bobJoin2, cancellationToken));
        Assert.True(alice.Groups.TryGet(record1.GroupId, out var aliceCreated1));
        Assert.Equal(GroupState.Created, aliceCreated1.State);

        var created2 = await alice.CreateGroupAsync("RaceTwo", [bobApp.Identity.Keys.IdentityKey, carol.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created2.Outcome);
        var record2 = created2.Group!.Record;
        var removed2 = await alice.RemoveGroupMemberAsync(record2.GroupId, carol.IdentityKey, cancellationToken);
        Assert.Equal(RemoveMemberOutcome.Removed, removed2.Outcome);
        Assert.Equal(GroupState.WaitingForMembers, removed2.Group!.State);

        using var wrongKey = GroupKey.Create();
        var wrongRecord = GroupRecord.Create(record2.GroupId, 1, record2.Name, wrongKey, [.. record2.Members], alice.Identity.Keys);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bobApp.Identity.Keys.IdentityKey, GroupJoin.Create(wrongRecord), cancellationToken));
        Assert.Equal(GroupState.WaitingForMembers, alice.Groups.TryGet(record2.GroupId, out var aliceStillWaiting) ? aliceStillWaiting.State : GroupState.Created);

        Assert.True(await alice.Groups.ReceiveJoinAsync(bobApp.Identity.Keys.IdentityKey, GroupJoin.Create(record2), cancellationToken));
        Assert.True(alice.Groups.TryGet(record2.GroupId, out var aliceCreated2));
        Assert.Equal(GroupState.Created, aliceCreated2.State);
        var pendingUpdate = Assert.IsType<GroupUpdate>(Assert.Single(alice.Groups.PendingNoticesFor(bobApp.Identity.Keys.IdentityKey), item => item.Message is GroupUpdate update && update.Record.GroupId.Equals(record2.GroupId)).Message);
        Assert.Equal(2, pendingUpdate.Record.Epoch);

        alice.Identity.Dispose();
        bobApp.Identity.Dispose();
    }

    private sealed class PausingGroupStore(InMemoryGroupStore inner) : IGroupStore
    {
        private TaskCompletionSource<bool>? advanceEntered;
        private TaskCompletionSource<bool>? advanceRelease;
        private TaskCompletionSource<bool>? saveEntered;
        private TaskCompletionSource<bool>? saveRelease;

        public Task<bool> ArmAdvanceEpoch()
        {
            advanceEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            advanceRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return advanceEntered.Task;
        }

        public void ReleaseAdvanceEpoch() => Interlocked.Exchange(ref advanceRelease, null)?.TrySetResult(true);

        public Task<bool> ArmSaveMembership()
        {
            saveEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            saveRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return saveEntered.Task;
        }

        public void ReleaseSaveMembership() => Interlocked.Exchange(ref saveRelease, null)?.TrySetResult(true);

        public ValueTask<IReadOnlyList<LoadedGroup>> LoadGroupsAsync(CancellationToken cancellationToken) =>
            inner.LoadGroupsAsync(cancellationToken);

        public ValueTask AddGroupAsync(GroupRecord record, GroupKey key, IReadOnlyList<GroupMemberStatus> members, CancellationToken cancellationToken) =>
            inner.AddGroupAsync(record, key, members, cancellationToken);

        public ValueTask UpdateMembersAsync(GroupId groupId, IReadOnlyCollection<GroupMemberStatus> members, CancellationToken cancellationToken) =>
            inner.UpdateMembersAsync(groupId, members, cancellationToken);

        public async ValueTask AdvanceEpochAsync(
            GroupRecord record,
            GroupKey key,
            Ed25519PublicKey authorityKey,
            IReadOnlyCollection<GroupMemberStatus> members,
            IReadOnlyCollection<Ed25519PublicKey> removeLeaveProofs,
            CancellationToken cancellationToken)
        {
            await inner.AdvanceEpochAsync(record, key, authorityKey, members, removeLeaveProofs, cancellationToken);
            if (Interlocked.Exchange(ref advanceEntered, null) is { } entered
                && Volatile.Read(ref advanceRelease) is { } release)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
        }

        public async ValueTask SaveMembershipAsync(
            GroupId groupId,
            Ed25519PublicKey authorityKey,
            IReadOnlyCollection<GroupMemberStatus> members,
            GroupLeave? saveLeaveProof,
            CancellationToken cancellationToken)
        {
            await inner.SaveMembershipAsync(groupId, authorityKey, members, saveLeaveProof, cancellationToken);
            if (Interlocked.Exchange(ref saveEntered, null) is { } entered
                && Volatile.Read(ref saveRelease) is { } release)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
        }

        public ValueTask<IReadOnlyList<GroupChatMessage>> LoadMessagesAsync(CancellationToken cancellationToken) =>
            inner.LoadMessagesAsync(cancellationToken);

        public ValueTask<bool> AddMessageAsync(GroupChatMessage message, CancellationToken cancellationToken) =>
            inner.AddMessageAsync(message, cancellationToken);

        public ValueTask<bool> MarkReceivedAsync(GroupId groupId, Ed25519PublicKey memberKey, MessageId id, CancellationToken cancellationToken) =>
            inner.MarkReceivedAsync(groupId, memberKey, id, cancellationToken);

        public ValueTask<IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>>> LoadDeletedAsync(CancellationToken cancellationToken) =>
            inner.LoadDeletedAsync(cancellationToken);

        public ValueTask<int> DeleteChatAsync(GroupId groupId, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken) =>
            inner.DeleteChatAsync(groupId, deleted, cancellationToken);

        public ValueTask<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, IReadOnlyList<AuthorSequenceRange> deleted, CancellationToken cancellationToken) =>
            inner.DeleteWaitingAsync(groupId, id, deleted, cancellationToken);

        public ValueTask<IReadOnlyList<GroupAcknowledgement>> LoadAcknowledgementsAsync(CancellationToken cancellationToken) =>
            inner.LoadAcknowledgementsAsync(cancellationToken);

        public ValueTask<bool> AddAcknowledgementAsync(GroupAcknowledgement acknowledgement, CancellationToken cancellationToken) =>
            inner.AddAcknowledgementAsync(acknowledgement, cancellationToken);

        public ValueTask<bool> RemoveAcknowledgementAsync(GroupId groupId, Ed25519PublicKey recipientKey, MessageId messageId, CancellationToken cancellationToken) =>
            inner.RemoveAcknowledgementAsync(groupId, recipientKey, messageId, cancellationToken);
    }

    private static Task<GroupKey?> AliceKeyAsync(Messenger messenger, GroupId groupId) =>
        Task.FromResult(messenger.Groups.TryGetKey(groupId, out _, out var key) ? key : null);

    private static async Task<GroupRecord> CreateTwoMemberAsync(Messenger alice, KeySet bob, string bobName, CancellationToken cancellationToken)
    {
        await AddContactAsync(alice, bob, bobName, cancellationToken);
        var created = await alice.CreateGroupAsync("Two", [bob.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bob.IdentityKey, GroupJoin.Create(created.Group!.Record), cancellationToken));
        return created.Group.Record;
    }

    private static async Task<GroupRecord> CreateThreeMemberAsync(Messenger alice, KeySet bob, string bobName, KeySet carol, string carolName, CancellationToken cancellationToken)
    {
        await AddContactAsync(alice, bob, bobName, cancellationToken);
        await AddContactAsync(alice, carol, carolName, cancellationToken);
        var created = await alice.CreateGroupAsync("Field team", [bob.IdentityKey, carol.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bob.IdentityKey, GroupJoin.Create(created.Group!.Record), cancellationToken));
        Assert.True(await alice.Groups.ReceiveJoinAsync(carol.IdentityKey, GroupJoin.Create(created.Group.Record), cancellationToken));
        return created.Group.Record;
    }

    private static async Task<GroupRecord> CreateFourMemberAsync(
        Messenger alice,
        KeySet bob,
        string bobName,
        KeySet carol,
        string carolName,
        KeySet dave,
        string daveName,
        CancellationToken cancellationToken)
    {
        await AddContactAsync(alice, bob, bobName, cancellationToken);
        await AddContactAsync(alice, carol, carolName, cancellationToken);
        await AddContactAsync(alice, dave, daveName, cancellationToken);
        var created = await alice.CreateGroupAsync("Squad", [bob.IdentityKey, carol.IdentityKey, dave.IdentityKey], cancellationToken);
        Assert.Equal(CreateGroupOutcome.Created, created.Outcome);
        Assert.True(await alice.Groups.ReceiveJoinAsync(bob.IdentityKey, GroupJoin.Create(created.Group!.Record), cancellationToken));
        Assert.True(await alice.Groups.ReceiveJoinAsync(carol.IdentityKey, GroupJoin.Create(created.Group.Record), cancellationToken));
        Assert.True(await alice.Groups.ReceiveJoinAsync(dave.IdentityKey, GroupJoin.Create(created.Group.Record), cancellationToken));
        return created.Group.Record;
    }

    private static Ipv4Address LoopbackIpv4()
    {
        Assert.True(Ipv4Address.TryCreate(127, 0, 0, 1, out var address));
        return address;
    }

    private static Task<Messenger> StartCopyPeerAsync(string name, LoopbackDiscovery.Node node, CancellationToken cancellationToken) =>
        Messenger.StartAsync(
            TestKeys.NewIdentity(name),
            MessengerSetup.InMemory() with
            {
                ListenAddress = IPAddress.Loopback,
                ListenPort = 0,
                CopyListenAddress = IPAddress.Loopback,
                CopyListenPort = 0,
                CopyAddressSource = static () => [LoopbackIpv4()],
                Connection = TestMessengers.Fast,
                Discovery = node.Setup,
            },
            cancellationToken);
}
