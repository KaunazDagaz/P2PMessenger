using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

public sealed class GroupBook : IDisposable
{
    private const int EarlyJoinLimit = 256;

    private static readonly Action<ILogger, string, Exception?> JoinFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(JoinFailed)),
        "Joining groups failed with {ExceptionType}; the app tries again when a contact changes.");

    private readonly OwnIdentity own;
    private readonly ContactBook contacts;
    private readonly IGroupStore store;
    private readonly ILogger logger;
    private readonly IFailureSink? failureSink;
    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<GroupId, Entry> groups = [];
    private readonly Dictionary<(GroupId Group, Ed25519PublicKey Member), GroupJoin> earlyJoins = [];
    private readonly HashSet<(GroupId Group, Ed25519PublicKey Peer, MessageId NoticeId)> confirmedProofs = [];
    private int disposed;

    internal GroupBook(OwnIdentity own, ContactBook contacts, IGroupStore store, IEnumerable<LoadedGroup> loaded, ILogger logger, IFailureSink? failures = null)
    {
        this.own = own;
        this.contacts = contacts;
        this.store = store;
        this.logger = logger;
        failureSink = failures;

        foreach (LoadedGroup group in loaded)
        {
            if (!group.History.Any(record => record.HasMember(own.Keys.IdentityKey))
                || !groups.TryAdd(group.Record.GroupId, new Entry(new Group(group.Record, group.Members, own.Keys.IdentityKey, group.AuthorityKey, group.History, group.LeaveProofs), group.Key)))
            {
                group.Key.Dispose();
                throw new ArgumentException("A loaded group does not include the user or is there twice.", nameof(loaded));
            }
        }

        contacts.Changed += OnContactChanged;
    }

    public event EventHandler<GroupChangedEventArgs>? Changed;

    internal event EventHandler<Ed25519PublicKey>? DeliveryNeeded;

    public IReadOnlyList<Group> All
    {
        get
        {
            lock (gate)
            {
                return [.. groups.Values.Select(static entry => entry.Group)];
            }
        }
    }

    public bool TryGet(GroupId groupId, [NotNullWhen(true)] out Group? group)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        lock (gate)
        {
            group = groups.TryGetValue(groupId, out Entry? entry) ? entry.Group : null;
            return group is not null;
        }
    }

    public IReadOnlyList<GroupMemberStatus> MembersToAdd(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return [.. group.Others.Where(member => !IsAddedContact(member.IdentityKey))];
    }

    public async Task<CreateGroupResult> CreateAsync(string? name, IReadOnlyCollection<Ed25519PublicKey> contactKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKeys);

        if (!DisplayName.TryCreate(name, out DisplayName? groupName))
        {
            return new CreateGroupResult(CreateGroupOutcome.NameNotValid, null);
        }

        List<Ed25519PublicKey> invited = [.. contactKeys.Distinct().Where(key => !key.Equals(own.Keys.IdentityKey))];
        if (invited.Count == 0)
        {
            return new CreateGroupResult(CreateGroupOutcome.NoMembers, null);
        }

        if (invited.Count + 1 > GroupRecord.MaxMembers)
        {
            return new CreateGroupResult(CreateGroupOutcome.TooManyMembers, null);
        }

        List<GroupMember> members = [new GroupMember(own.Keys.IdentityKey, own.Name)];
        foreach (Ed25519PublicKey key in invited)
        {
            if (!contacts.TryGet(key, out Contact? contact) || !contact.IsAdded)
            {
                return new CreateGroupResult(CreateGroupOutcome.NotAnAddedContact, null);
            }

            members.Add(new GroupMember(contact.IdentityKey, contact.Name));
        }

        GroupKey groupKey = GroupKey.Create();
        Group group;
        try
        {
            GroupRecord record = GroupRecord.Create(GroupId.Create(), 1, groupName, groupKey, members, own.Keys);
            List<GroupMemberStatus> statuses =
            [
                .. record.Members.Select(member => member.IdentityKey.Equals(own.Keys.IdentityKey)
                    ? new GroupMemberStatus(member.IdentityKey, member.Name, MemberJoin.Joined, NoticeState.None, NoticeState.None)
                    : new GroupMemberStatus(member.IdentityKey, member.Name, MemberJoin.NotYet, NoticeState.Waiting, NoticeState.None)),
            ];
            group = new Group(record, statuses, own.Keys.IdentityKey);

            await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await store.AddGroupAsync(record, groupKey, statuses, cancellationToken).ConfigureAwait(false);
                lock (gate)
                {
                    groups.Add(record.GroupId, new Entry(group, groupKey));
                }
            }
            finally
            {
                writing.Release();
            }
        }
        catch
        {
            groupKey.Dispose();
            throw;
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(group));
        foreach (GroupMemberStatus member in group.Others)
        {
            DeliveryNeeded?.Invoke(this, member.IdentityKey);
        }

        return new CreateGroupResult(CreateGroupOutcome.Created, group);
    }

    public async Task<RemoveMemberResult> RemoveMemberAsync(GroupId groupId, Ed25519PublicKey memberKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(memberKey);

        Group updated;
        List<Ed25519PublicKey> notify = [];
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetEntry(groupId, out Entry? entry))
            {
                return new RemoveMemberResult(RemoveMemberOutcome.NotAGroup, null);
            }

            Group current = entry.Group;
            if (!current.IsAuthority)
            {
                return new RemoveMemberResult(RemoveMemberOutcome.NotAuthorized, null);
            }

            if (current.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer)
            {
                return new RemoveMemberResult(RemoveMemberOutcome.NotActive, null);
            }

            if (memberKey.Equals(own.Keys.IdentityKey))
            {
                return new RemoveMemberResult(RemoveMemberOutcome.CannotRemoveSelf, null);
            }

            if (current.Find(memberKey) is not { } target)
            {
                return new RemoveMemberResult(RemoveMemberOutcome.NotAMember, null);
            }

            updated = await RotateOnDepartureAsync(entry, target, GroupMemberState.Removed, null, cancellationToken).ConfigureAwait(false);
            foreach (GroupMemberStatus member in updated.AllMembers)
            {
                if (!member.IdentityKey.Equals(own.Keys.IdentityKey)
                    && (member.Update == NoticeState.Waiting || member.Removed == NoticeState.Waiting))
                {
                    notify.Add(member.IdentityKey);
                }
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        foreach (Ed25519PublicKey key in notify.Distinct())
        {
            DeliveryNeeded?.Invoke(this, key);
        }

        if (updated.State == GroupState.Joining)
        {
            await JoinWhereReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RemoveMemberResult(updated.IsEnded ? RemoveMemberOutcome.EndedGroup : RemoveMemberOutcome.Removed, updated);
    }

    public async Task<LeaveGroupOutcome> LeaveAsync(GroupId groupId, Ed25519PublicKey? successorKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        Group updated;
        List<Ed25519PublicKey> notify = [];
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetEntry(groupId, out Entry? entry))
            {
                return LeaveGroupOutcome.NotAGroup;
            }

            Group current = entry.Group;
            if (!current.CanLeave)
            {
                return LeaveGroupOutcome.NotActive;
            }

            if (current.NeedsSuccessorToLeave)
            {
                if (successorKey is null)
                {
                    return LeaveGroupOutcome.SuccessorRequired;
                }

                if (successorKey.Equals(own.Keys.IdentityKey) || current.Find(successorKey) is null)
                {
                    return LeaveGroupOutcome.InvalidSuccessor;
                }
            }
            else if (successorKey is not null)
            {
                return LeaveGroupOutcome.InvalidSuccessor;
            }

            GroupLeave leave = GroupLeave.Create(current.Id, current.Record.Epoch, own.Keys, successorKey);
            Ed25519PublicKey newAuthority = successorKey ?? current.AuthorityKey;
            List<GroupMemberStatus> changed = [];
            foreach (GroupMemberStatus member in current.AllMembers)
            {
                if (member.IdentityKey.Equals(own.Keys.IdentityKey))
                {
                    changed.Add(ClearNotices(member) with { State = GroupMemberState.Left });
                }
                else if (member.State == GroupMemberState.Member)
                {
                    changed.Add(ClearNotices(member) with { Leave = NoticeState.Waiting });
                    notify.Add(member.IdentityKey);
                }
                else
                {
                    changed.Add(ClearNotices(member));
                }
            }

            await store.SaveMembershipAsync(current.Id, newAuthority, changed, leave, cancellationToken).ConfigureAwait(false);
            updated = current.WithMembership(newAuthority, changed, UpsertProof(current.LeaveProofs, leave));
            lock (gate)
            {
                groups[current.Id] = entry with { Group = updated };
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        foreach (Ed25519PublicKey key in notify)
        {
            DeliveryNeeded?.Invoke(this, key);
        }

        return LeaveGroupOutcome.Left;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        contacts.Changed -= OnContactChanged;
        writing.Dispose();
        lock (gate)
        {
            foreach (Entry entry in groups.Values)
            {
                entry.Key.Dispose();
            }

            groups.Clear();
        }
    }

    internal async Task JoinWhereReadyAsync(CancellationToken cancellationToken)
    {
        List<Group> joined = [];
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (Group group in All)
            {
                if (group.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer
                    || group.YouJoined
                    || group.Others.Any(member => !IsAddedContact(member.IdentityKey)))
                {
                    continue;
                }

                List<GroupMemberStatus> changed =
                [
                    group.Find(own.Keys.IdentityKey)! with { Joined = MemberJoin.Joined },
                    .. group.Others.Select(static member => member with { OwnJoin = NoticeState.Waiting }),
                ];
                await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
                joined.Add(Replace(group.Id, changed));
            }
        }
        finally
        {
            writing.Release();
        }

        foreach (Group group in joined)
        {
            Changed?.Invoke(this, new GroupChangedEventArgs(group));
            foreach (GroupMemberStatus member in group.Others)
            {
                DeliveryNeeded?.Invoke(this, member.IdentityKey);
            }
        }
    }

    internal async Task<bool> ReceiveInvitationAsync(Ed25519PublicKey senderKey, GroupInvitation invitation, CancellationToken cancellationToken)
    {
        GroupRecord record = invitation.Record;
        if (record.Epoch != 1 || !record.Signer.Equals(senderKey) || !record.HasMember(own.Keys.IdentityKey))
        {
            return true;
        }

        Group? added = null;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (groups.ContainsKey(record.GroupId))
                {
                    return true;
                }
            }

            GroupKey groupKey = invitation.OpenKey();
            try
            {
                List<GroupMemberStatus> statuses =
                [
                    .. record.Members.Select(member => new GroupMemberStatus(
                        member.IdentityKey,
                        member.Name,
                        member.IdentityKey.Equals(record.Signer) ? MemberJoin.Joined : EarlyJoinOf(record, member.IdentityKey),
                        NoticeState.None,
                        NoticeState.None)),
                ];
                await store.AddGroupAsync(record, groupKey, statuses, cancellationToken).ConfigureAwait(false);
                added = new Group(record, statuses, own.Keys.IdentityKey);
                lock (gate)
                {
                    groups.Add(record.GroupId, new Entry(added, groupKey));
                }
            }
            catch
            {
                groupKey.Dispose();
                throw;
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(added));
        await JoinWhereReadyAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal async Task<bool> ReceiveJoinAsync(Ed25519PublicKey senderKey, GroupJoin join, CancellationToken cancellationToken)
    {
        Group? updated = null;
        Ed25519PublicKey? notifyStale = null;
        bool? earlyResult = null;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGet(join.GroupId, out Group? group))
            {
                KeepEarly(senderKey, join);
                return false;
            }

            if (group.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer)
            {
                return group.FindAny(senderKey) is not null;
            }

            if (join.Epoch < group.Record.Epoch && group.Record.Epoch >= 2)
            {
                if (group.Find(senderKey) is { Joined: MemberJoin.NotYet } activeMember
                    && !activeMember.IdentityKey.Equals(own.Keys.IdentityKey)
                    && group.History.Any(record => record.Epoch == join.Epoch && record.Hash.SequenceEqual(join.RecordHash)))
                {
                    GroupMemberStatus[] changed = [activeMember with { Joined = MemberJoin.Joined }];
                    await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
                    updated = Replace(group.Id, changed);
                    group = updated;
                }

                notifyStale = await QueueStaleNoticeLockedAsync(group, senderKey, cancellationToken).ConfigureAwait(false);
                if (updated is not null && TryGet(join.GroupId, out Group? latest))
                {
                    updated = latest;
                }

                earlyResult = group.FindAny(senderKey) is not null;
            }
            else if (group.Find(senderKey) is { } member && !member.IdentityKey.Equals(own.Keys.IdentityKey))
            {
                MemberJoin joined = JoinOf(group.Record, join);
                if (member.Joined != joined && member.Joined != MemberJoin.JoinedAnotherList)
                {
                    GroupMemberStatus[] changed = [member with { Joined = joined }];
                    await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
                    updated = Replace(group.Id, changed);
                }
            }
        }
        finally
        {
            writing.Release();
        }

        if (updated is not null)
        {
            Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        }

        if (notifyStale is not null)
        {
            DeliveryNeeded?.Invoke(this, notifyStale);
        }

        return earlyResult ?? true;
    }

    internal async Task<bool> ReceiveUpdateAsync(Ed25519PublicKey senderKey, GroupUpdate update, CancellationToken cancellationToken)
    {
        GroupRecord incoming = update.Record;
        if (!incoming.HasMember(own.Keys.IdentityKey))
        {
            return false;
        }

        Group? updated = null;
        List<Ed25519PublicKey> notify = [];
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetEntry(incoming.GroupId, out Entry? entry))
            {
                return false;
            }

            Group current = entry.Group;
            if (current.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer)
            {
                return current.FindAny(senderKey) is not null;
            }

            if (incoming.Equals(current.Record))
            {
                if (current.Find(senderKey) is { Update: NoticeState.Waiting } member)
                {
                    GroupMemberStatus[] acked = [member with { Update = NoticeState.Received }];
                    await store.UpdateMembersAsync(current.Id, acked, cancellationToken).ConfigureAwait(false);
                    updated = Replace(current.Id, acked);
                }

                return true;
            }

            if (current.Find(senderKey) is not { } activeSender)
            {
                return false;
            }

            if (incoming.Epoch < current.Record.Epoch)
            {
                if (await QueueStaleNoticeLockedAsync(current, senderKey, cancellationToken).ConfigureAwait(false) is { } stalePeer)
                {
                    notify.Add(stalePeer);
                }
            }
            else if (incoming.Epoch <= current.Record.Epoch
                || !incoming.Signer.Equals(current.AuthorityKey)
                || !incoming.Name.Equals(current.Name)
                || !IsStrictSubset(incoming.Members, current.Record.Members))
            {
                return false;
            }
            else
            {
                GroupKey nextKey = update.OpenKey();
                try
                {
                    List<GroupMemberStatus> changed = [];
                    List<Ed25519PublicKey> removeProofs = [];
                    bool allRemainingJoined = current.AllMembers
                        .Where(member => incoming.HasMember(member.IdentityKey))
                        .All(member => member.IdentityKey.Equals(own.Keys.IdentityKey) || member.Joined == MemberJoin.Joined || member.IdentityKey.Equals(incoming.Signer));
                    bool ownJoined = current.YouJoined || incoming.Signer.Equals(own.Keys.IdentityKey);
                    foreach (GroupMemberStatus member in current.AllMembers)
                    {
                        if (incoming.HasMember(member.IdentityKey))
                        {
                            MemberJoin joined = member.IdentityKey.Equals(incoming.Signer) ? MemberJoin.Joined : member.Joined;
                            NoticeState ownJoinState = !member.IdentityKey.Equals(own.Keys.IdentityKey)
                                && ownJoined
                                && (!current.IsCreated || !allRemainingJoined || member.OwnJoin == NoticeState.Waiting)
                                ? NoticeState.Waiting
                                : NoticeState.None;
                            NoticeState updateState = member.IdentityKey.Equals(own.Keys.IdentityKey) || member.IdentityKey.Equals(senderKey)
                                ? NoticeState.None
                                : NoticeState.Waiting;
                            changed.Add(member with
                            {
                                State = GroupMemberState.Member,
                                Joined = joined,
                                Invitation = NoticeState.None,
                                OwnJoin = ownJoinState,
                                Update = updateState,
                                Leave = NoticeState.None,
                                Removed = NoticeState.None,
                                RemovedEpoch = 0,
                            });
                            if (updateState == NoticeState.Waiting || ownJoinState == NoticeState.Waiting)
                            {
                                notify.Add(member.IdentityKey);
                            }
                        }
                        else if (member.State == GroupMemberState.Member)
                        {
                            changed.Add(ClearNotices(member) with
                            {
                                State = GroupMemberState.Removed,
                                RemovedEpoch = incoming.Epoch,
                            });
                        }
                        else
                        {
                            changed.Add(ClearNotices(member) with
                            {
                                RemovedEpoch = member.State == GroupMemberState.Removed ? member.RemovedEpoch : 0,
                            });
                        }
                    }

                    foreach (GroupLeave proof in current.LeaveProofs)
                    {
                        if (!proof.HasSuccessor && !incoming.HasMember(proof.MemberKey))
                        {
                            removeProofs.Add(proof.MemberKey);
                        }
                    }

                    await store.AdvanceEpochAsync(incoming, nextKey, current.AuthorityKey, changed, removeProofs, cancellationToken).ConfigureAwait(false);
                    List<GroupLeave> remainingProofs = [.. current.LeaveProofs.Where(proof => !removeProofs.Contains(proof.MemberKey))];
                    updated = current.WithEpoch(incoming, current.AuthorityKey, changed, remainingProofs);
                    lock (gate)
                    {
                        groups[current.Id] = new Entry(updated, nextKey);
                    }

                    entry.Key.Dispose();
                }
                catch
                {
                    nextKey.Dispose();
                    throw;
                }
            }
        }
        finally
        {
            writing.Release();
        }

        if (updated is not null)
        {
            Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        }

        foreach (Ed25519PublicKey key in notify.Distinct())
        {
            DeliveryNeeded?.Invoke(this, key);
        }

        if (updated is not null && updated.State == GroupState.Joining)
        {
            await JoinWhereReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    internal async Task<bool> ReceiveLeaveAsync(Ed25519PublicKey senderKey, GroupLeave leave, CancellationToken cancellationToken)
    {
        if (!leave.HasValidSignature())
        {
            return false;
        }

        Group? updated = null;
        List<Ed25519PublicKey> notify = [];
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetEntry(leave.GroupId, out Entry? entry))
            {
                return false;
            }

            Group current = entry.Group;
            if (current.FindAny(senderKey) is null || current.FindAny(leave.MemberKey) is null)
            {
                return false;
            }

            if (current.LeaveProofs.Any(proof => proof.Equals(leave)))
            {
                return true;
            }

            if (current.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer)
            {
                return true;
            }

            if (current.FindAny(leave.MemberKey) is { State: GroupMemberState.Left or GroupMemberState.Removed })
            {
                return !leave.HasSuccessor;
            }

            if (!leave.HasSuccessor && leave.Epoch > current.Record.Epoch)
            {
                return false;
            }

            GroupMemberStatus leaver = current.Find(leave.MemberKey)!;
            if (leave.HasSuccessor)
            {
                if (leave.Epoch < current.Record.Epoch
                    || !leave.MemberKey.Equals(current.AuthorityKey)
                    || leave.SuccessorKey is not { } successor
                    || current.Find(successor) is null
                    || successor.Equals(leave.MemberKey))
                {
                    return false;
                }

                if (successor.Equals(own.Keys.IdentityKey))
                {
                    List<GroupMemberStatus> handoverMembers =
                    [
                        .. current.AllMembers.Select(member => member.IdentityKey.Equals(leaver.IdentityKey)
                            ? ClearNotices(member) with { State = GroupMemberState.Left }
                            : member),
                    ];
                    await store.SaveMembershipAsync(current.Id, successor, handoverMembers, leave, cancellationToken).ConfigureAwait(false);
                    Group handoverGroup = current.WithMembership(successor, handoverMembers, UpsertProof(current.LeaveProofs, leave));
                    Entry handoverEntry = entry with { Group = handoverGroup };
                    lock (gate)
                    {
                        groups[current.Id] = handoverEntry;
                    }

                    updated = await RotateOnDepartureAsync(handoverEntry, leaver, GroupMemberState.Left, leave, cancellationToken).ConfigureAwait(false);
                    foreach (GroupMemberStatus member in updated.Others)
                    {
                        if (member.Update == NoticeState.Waiting)
                        {
                            notify.Add(member.IdentityKey);
                        }
                    }
                }
                else
                {
                    List<GroupMemberStatus> changed =
                    [
                        .. current.AllMembers.Select(member => member.IdentityKey.Equals(leaver.IdentityKey)
                            ? ClearNotices(member) with { State = GroupMemberState.Left }
                            : member),
                    ];
                    await store.SaveMembershipAsync(current.Id, successor, changed, leave, cancellationToken).ConfigureAwait(false);
                    updated = current.WithMembership(successor, changed, UpsertProof(current.LeaveProofs, leave));
                    lock (gate)
                    {
                        groups[current.Id] = entry with { Group = updated };
                    }

                    if (!senderKey.Equals(successor))
                    {
                        notify.Add(successor);
                    }
                }
            }
            else if (leave.MemberKey.Equals(current.AuthorityKey))
            {
                int remainingActive = current.Members.Count - 1;
                if (remainingActive >= GroupRecord.MinMembers)
                {
                    return false;
                }

                updated = await EndOnDepartureAsync(entry, leaver, GroupMemberState.Left, leave, current.AuthorityKey, cancellationToken).ConfigureAwait(false);
            }
            else if (current.IsAuthority)
            {
                updated = await RotateOnDepartureAsync(entry, leaver, GroupMemberState.Left, null, cancellationToken).ConfigureAwait(false);
                foreach (GroupMemberStatus member in updated.Others)
                {
                    if (member.Update == NoticeState.Waiting)
                    {
                        notify.Add(member.IdentityKey);
                    }
                }
            }
            else
            {
                List<GroupMemberStatus> changed =
                [
                    .. current.AllMembers.Select(member => member.IdentityKey.Equals(leaver.IdentityKey)
                        ? ClearNotices(member) with { State = GroupMemberState.Left }
                        : member),
                ];
                await store.SaveMembershipAsync(current.Id, current.AuthorityKey, changed, leave, cancellationToken).ConfigureAwait(false);
                updated = current.WithMembership(current.AuthorityKey, changed, UpsertProof(current.LeaveProofs, leave));
                lock (gate)
                {
                    groups[current.Id] = entry with { Group = updated };
                }

                if (!senderKey.Equals(current.AuthorityKey))
                {
                    notify.Add(current.AuthorityKey);
                }
            }
        }
        finally
        {
            writing.Release();
        }

        if (updated is not null)
        {
            Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        }

        foreach (Ed25519PublicKey key in notify.Distinct())
        {
            DeliveryNeeded?.Invoke(this, key);
        }

        if (updated is not null && updated.State == GroupState.Joining)
        {
            await JoinWhereReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    internal async Task<bool> ReceiveRemovedAsync(Ed25519PublicKey senderKey, GroupRemoved removed, CancellationToken cancellationToken)
    {
        if (!removed.HasValidSignature() || !removed.RemovedMemberKey.Equals(own.Keys.IdentityKey))
        {
            return false;
        }

        Group? updated = null;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetEntry(removed.GroupId, out Entry? entry))
            {
                return removed.SignerKey.Equals(senderKey);
            }

            Group current = entry.Group;
            if (current.State is GroupState.Removed or GroupState.Left or GroupState.Ended)
            {
                return current.FindAny(senderKey) is not null;
            }

            if (!removed.SignerKey.Equals(current.AuthorityKey) || removed.NewEpoch <= current.Record.Epoch)
            {
                return false;
            }

            List<GroupMemberStatus> changed =
            [
                .. current.AllMembers.Select(member => member.IdentityKey.Equals(own.Keys.IdentityKey)
                    ? ClearNotices(member) with { State = GroupMemberState.Removed, RemovedEpoch = removed.NewEpoch }
                    : ClearNotices(member)),
            ];
            await store.SaveMembershipAsync(current.Id, current.AuthorityKey, changed, null, cancellationToken).ConfigureAwait(false);
            updated = current.WithMembership(current.AuthorityKey, changed, current.LeaveProofs);
            lock (gate)
            {
                groups[current.Id] = entry with { Group = updated };
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        return true;
    }

    internal async Task NotifyPeerStaleEpochAsync(Ed25519PublicKey peerKey, GroupId groupId, int peerEpoch, CancellationToken cancellationToken)
    {
        Ed25519PublicKey? notify = null;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGet(groupId, out Group? group) || group.Record.Epoch < 2 || peerEpoch >= group.Record.Epoch)
            {
                return;
            }

            notify = await QueueStaleNoticeLockedAsync(group, peerKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }

        if (notify is not null)
        {
            DeliveryNeeded?.Invoke(this, notify);
        }
    }

    internal async Task<bool> MarkNoticeReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        Group? updated = null;
        bool matchedProof = false;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (Group group in All)
            {
                if (group.FindAny(contactKey) is not { } member || contactKey.Equals(own.Keys.IdentityKey))
                {
                    continue;
                }

                GroupLeave? matchingProof = group.LeaveProofs.FirstOrDefault(proof => proof.NoticeId.Equals(id));
                if (matchingProof is not null && !group.IsLeft)
                {
                    lock (gate)
                    {
                        if (confirmedProofs.Add((group.Id, contactKey, id)))
                        {
                            matchedProof = true;
                        }
                    }

                    if (matchedProof)
                    {
                        break;
                    }
                }

                GroupMemberStatus received = member;
                if (member.State == GroupMemberState.Member)
                {
                    if (member.Invitation == NoticeState.Waiting && group.YouCreated && group.Record.Epoch == 1 && GroupNotices.InvitationId(group.Record).Equals(id))
                    {
                        received = received with { Invitation = NoticeState.Received };
                    }

                    if (member.OwnJoin == NoticeState.Waiting && !group.IsLeft && !group.IsRemoved && !group.IsEnded && GroupNotices.JoinId(group.Record.Hash, own.Keys.IdentityKey).Equals(id))
                    {
                        received = received with { OwnJoin = NoticeState.Received };
                    }

                    if (member.Update == NoticeState.Waiting && group.Record.Epoch >= 2 && !group.IsLeft && !group.IsRemoved && !group.IsEnded && GroupNotices.UpdateId(group.Record).Equals(id))
                    {
                        received = received with { Update = NoticeState.Received };
                    }

                    if (member.Leave == NoticeState.Waiting && group.IsLeft)
                    {
                        GroupLeave? ownProof = group.LeaveProofs.FirstOrDefault(proof => proof.MemberKey.Equals(own.Keys.IdentityKey));
                        if (ownProof is not null && ownProof.NoticeId.Equals(id))
                        {
                            received = received with { Leave = NoticeState.Received };
                        }
                    }
                }
                else if (member.State == GroupMemberState.Removed
                    && member.Removed == NoticeState.Waiting
                    && member.RemovedEpoch >= 2
                    && group.IsAuthority
                    && GroupNotices.RemovedId(group.Id, member.RemovedEpoch, contactKey, own.Keys.IdentityKey).Equals(id))
                {
                    received = received with { Removed = NoticeState.Received };
                }

                if (received != member)
                {
                    GroupMemberStatus[] changed = [received];
                    await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
                    updated = Replace(group.Id, changed);
                    break;
                }
            }
        }
        finally
        {
            writing.Release();
        }

        if (updated is not null)
        {
            Changed?.Invoke(this, new GroupChangedEventArgs(updated));
            return true;
        }

        return matchedProof;
    }

    internal IReadOnlyList<PendingSend> PendingNoticesFor(Ed25519PublicKey contactKey)
    {
        List<PendingSend> pending = [];
        lock (gate)
        {
            foreach (Entry entry in groups.Values)
            {
                Group group = entry.Group;
                if (group.FindAny(contactKey) is not { } member || contactKey.Equals(own.Keys.IdentityKey))
                {
                    continue;
                }

                if (group.IsLeft)
                {
                    if (member.State == GroupMemberState.Member && member.Leave == NoticeState.Waiting)
                    {
                        GroupLeave? ownProof = group.LeaveProofs.FirstOrDefault(proof => proof.MemberKey.Equals(own.Keys.IdentityKey));
                        if (ownProof is not null)
                        {
                            pending.Add(new PendingSend(ownProof.NoticeId, ownProof));
                        }
                    }

                    continue;
                }

                if (group.IsRemoved || group.ListsDiffer)
                {
                    continue;
                }

                if (group.IsEnded)
                {
                    if (group.IsAuthority && member.State == GroupMemberState.Removed && member.Removed == NoticeState.Waiting && member.RemovedEpoch >= 2)
                    {
                        AddHandoverProofsLocked(group, contactKey, pending);
                        GroupRemoved removed = GroupRemoved.Create(group.Id, member.RemovedEpoch, contactKey, own.Keys);
                        pending.Add(new PendingSend(removed.NoticeId, removed));
                    }

                    continue;
                }

                if (member.State == GroupMemberState.Removed)
                {
                    if (group.IsAuthority && member.Removed == NoticeState.Waiting && member.RemovedEpoch >= 2)
                    {
                        AddHandoverProofsLocked(group, contactKey, pending);
                        GroupRemoved removed = GroupRemoved.Create(group.Id, member.RemovedEpoch, contactKey, own.Keys);
                        pending.Add(new PendingSend(removed.NoticeId, removed));
                    }

                    continue;
                }

                if (member.State != GroupMemberState.Member)
                {
                    continue;
                }

                if (member.Update == NoticeState.Waiting && group.Record.Epoch >= 2)
                {
                    AddHandoverProofsLocked(group, contactKey, pending);
                    GroupUpdate update = GroupUpdate.Create(entry.Key, group.Record);
                    pending.Add(new PendingSend(update.NoticeId, update));
                }

                if (!group.IsAuthority && contactKey.Equals(group.AuthorityKey))
                {
                    foreach (GroupLeave proof in group.LeaveProofs.OrderBy(static proof => proof.Epoch))
                    {
                        if (!confirmedProofs.Contains((group.Id, contactKey, proof.NoticeId))
                            && (!proof.HasSuccessor || contactKey.Equals(proof.SuccessorKey)))
                        {
                            pending.Add(new PendingSend(proof.NoticeId, proof));
                        }
                    }
                }

                if (group.YouCreated && group.Record.Epoch == 1 && member.Invitation == NoticeState.Waiting)
                {
                    pending.Add(new PendingSend(GroupNotices.InvitationId(group.Record), GroupInvitation.Create(entry.Key, group.Record)));
                }

                if (member.OwnJoin == NoticeState.Waiting)
                {
                    pending.Add(new PendingSend(GroupNotices.JoinId(group.Record.Hash, own.Keys.IdentityKey), GroupJoin.Create(group.Record)));
                }
            }
        }

        return pending;
    }

    internal bool IsNoticePending(Ed25519PublicKey contactKey, MessageId id) =>
        PendingNoticesFor(contactKey).Any(pending => pending.Id.Equals(id));

    internal IReadOnlyList<Ed25519PublicKey> ContactsWithPendingNotices()
    {
        lock (gate)
        {
            HashSet<Ed25519PublicKey> keys = [];
            foreach (Entry entry in groups.Values)
            {
                Group group = entry.Group;
                foreach (GroupMemberStatus member in group.AllMembers)
                {
                    if (member.IdentityKey.Equals(own.Keys.IdentityKey))
                    {
                        continue;
                    }

                    if (member.Leave == NoticeState.Waiting
                        || member.Update == NoticeState.Waiting
                        || member.Removed == NoticeState.Waiting
                        || member.OwnJoin == NoticeState.Waiting
                        || (group.YouCreated && group.Record.Epoch == 1 && member.Invitation == NoticeState.Waiting)
                        || (!group.IsLeft && !group.IsRemoved && !group.IsEnded && !group.IsAuthority && member.IdentityKey.Equals(group.AuthorityKey)
                            && group.LeaveProofs.Any(proof => !confirmedProofs.Contains((group.Id, member.IdentityKey, proof.NoticeId))
                                && (!proof.HasSuccessor || member.IdentityKey.Equals(proof.SuccessorKey)))))
                    {
                        keys.Add(member.IdentityKey);
                    }
                }
            }

            return [.. keys];
        }
    }

    internal bool TryGetKey(GroupId groupId, [NotNullWhen(true)] out Group? group, [NotNullWhen(true)] out GroupKey? key)
    {
        lock (gate)
        {
            if (groups.TryGetValue(groupId, out Entry? entry))
            {
                group = entry.Group;
                key = entry.Key;
                return true;
            }
        }

        group = null;
        key = null;
        return false;
    }

    internal async Task<TResult> WithLockedGroupKeyAsync<TResult>(
        GroupId groupId,
        Func<Group?, GroupKey?, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(action);

        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Group? group;
            GroupKey? key;
            lock (gate)
            {
                if (groups.TryGetValue(groupId, out Entry? entry))
                {
                    group = entry.Group;
                    key = entry.Key;
                }
                else
                {
                    group = null;
                    key = null;
                }
            }

            return await action(group, key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }

    private static bool IsStrictSubset(IReadOnlyList<GroupMember> candidate, IReadOnlyList<GroupMember> superset) =>
        candidate.Count < superset.Count
        && candidate.All(member => superset.Any(existing => existing.IdentityKey.Equals(member.IdentityKey)));

    private static GroupMemberStatus ClearNotices(GroupMemberStatus member) =>
        member with
        {
            Invitation = NoticeState.None,
            OwnJoin = NoticeState.None,
            Update = NoticeState.None,
            Leave = NoticeState.None,
            Removed = NoticeState.None,
        };

    private static List<GroupLeave> UpsertProof(IReadOnlyList<GroupLeave> existing, GroupLeave proof) =>
    [
        .. existing.Where(item => !item.MemberKey.Equals(proof.MemberKey)),
        proof,
    ];

    private static MemberJoin JoinOf(GroupRecord record, GroupJoin join) =>
        join.Epoch == record.Epoch && join.RecordHash.SequenceEqual(record.Hash) ? MemberJoin.Joined : MemberJoin.JoinedAnotherList;

    private async Task<Group> RotateOnDepartureAsync(
        Entry entry,
        GroupMemberStatus departing,
        GroupMemberState departingState,
        GroupLeave? saveHandoverProof,
        CancellationToken cancellationToken)
    {
        Group current = entry.Group;
        List<GroupMemberStatus> remaining =
        [
            .. current.Members.Where(member => !member.IdentityKey.Equals(departing.IdentityKey)),
        ];
        if (remaining.Count < GroupRecord.MinMembers)
        {
            return await EndOnDepartureAsync(entry, departing, departingState, saveHandoverProof, current.AuthorityKey, cancellationToken).ConfigureAwait(false);
        }

        int nextEpoch = current.Record.Epoch + 1;
        List<GroupMember> nextRecordMembers = [.. remaining.Select(static member => new GroupMember(member.IdentityKey, member.Name))];
        GroupKey nextKey = GroupKey.Create();
        try
        {
            GroupRecord nextRecord = GroupRecord.Create(current.Id, nextEpoch, current.Name, nextKey, nextRecordMembers, own.Keys);
            bool allRemainingJoined = remaining.All(member => member.IdentityKey.Equals(own.Keys.IdentityKey) || member.Joined == MemberJoin.Joined);
            List<GroupMemberStatus> changed = [];
            List<Ed25519PublicKey> removeProofs = [];
            foreach (GroupMemberStatus member in current.AllMembers)
            {
                if (member.IdentityKey.Equals(departing.IdentityKey))
                {
                    if (departingState == GroupMemberState.Removed)
                    {
                        changed.Add(ClearNotices(member) with
                        {
                            State = GroupMemberState.Removed,
                            Removed = NoticeState.Waiting,
                            RemovedEpoch = nextEpoch,
                        });
                    }
                    else
                    {
                        changed.Add(ClearNotices(member) with { State = GroupMemberState.Left, RemovedEpoch = 0 });
                    }
                }
                else if (member.State == GroupMemberState.Member)
                {
                    MemberJoin joined = member.IdentityKey.Equals(own.Keys.IdentityKey) && allRemainingJoined
                        ? MemberJoin.Joined
                        : member.Joined;
                    NoticeState ownJoinState = !member.IdentityKey.Equals(own.Keys.IdentityKey) && current.YouJoined && !allRemainingJoined
                        ? NoticeState.Waiting
                        : NoticeState.None;
                    NoticeState updateState = member.IdentityKey.Equals(own.Keys.IdentityKey) ? NoticeState.None : NoticeState.Waiting;
                    changed.Add(member with
                    {
                        Joined = joined,
                        Invitation = NoticeState.None,
                        OwnJoin = ownJoinState,
                        Update = updateState,
                        Leave = NoticeState.None,
                        Removed = NoticeState.None,
                        RemovedEpoch = 0,
                    });
                }
                else
                {
                    changed.Add(member);
                }
            }

            foreach (GroupLeave proof in current.LeaveProofs)
            {
                if (!proof.HasSuccessor && (proof.MemberKey.Equals(departing.IdentityKey) || !nextRecord.HasMember(proof.MemberKey)))
                {
                    removeProofs.Add(proof.MemberKey);
                }
            }

            await store.AdvanceEpochAsync(nextRecord, nextKey, own.Keys.IdentityKey, changed, removeProofs, cancellationToken).ConfigureAwait(false);
            List<GroupLeave> remainingProofs = [.. current.LeaveProofs.Where(proof => !removeProofs.Contains(proof.MemberKey))];
            Group updated = current.WithEpoch(nextRecord, own.Keys.IdentityKey, changed, remainingProofs);
            lock (gate)
            {
                groups[current.Id] = new Entry(updated, nextKey);
            }

            entry.Key.Dispose();
            return updated;
        }
        catch
        {
            nextKey.Dispose();
            throw;
        }
    }

    private async Task<Group> EndOnDepartureAsync(
        Entry entry,
        GroupMemberStatus departing,
        GroupMemberState departingState,
        GroupLeave? saveLeaveProof,
        Ed25519PublicKey authorityKey,
        CancellationToken cancellationToken)
    {
        Group current = entry.Group;
        int nextEpoch = current.Record.Epoch + 1;
        List<GroupMemberStatus> changed = [];
        foreach (GroupMemberStatus member in current.AllMembers)
        {
            if (member.IdentityKey.Equals(departing.IdentityKey))
            {
                if (departingState == GroupMemberState.Removed)
                {
                    changed.Add(ClearNotices(member) with
                    {
                        State = GroupMemberState.Removed,
                        Removed = NoticeState.Waiting,
                        RemovedEpoch = nextEpoch,
                    });
                }
                else
                {
                    changed.Add(ClearNotices(member) with { State = GroupMemberState.Left, RemovedEpoch = 0 });
                }
            }
            else
            {
                changed.Add(ClearNotices(member));
            }
        }

        await store.SaveMembershipAsync(current.Id, authorityKey, changed, saveLeaveProof, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GroupLeave> proofs = saveLeaveProof is null ? current.LeaveProofs : UpsertProof(current.LeaveProofs, saveLeaveProof);
        Group updated = current.WithMembership(authorityKey, changed, proofs);
        lock (gate)
        {
            groups[current.Id] = entry with { Group = updated };
        }

        return updated;
    }

    private async Task<Ed25519PublicKey?> QueueStaleNoticeLockedAsync(Group group, Ed25519PublicKey peerKey, CancellationToken cancellationToken)
    {
        if (group.Record.Epoch < 2 || peerKey.Equals(own.Keys.IdentityKey) || group.State is GroupState.Left or GroupState.Removed or GroupState.Ended or GroupState.ListsDiffer)
        {
            return null;
        }

        if (group.Find(peerKey) is { } activeMember)
        {
            if (activeMember.Update == NoticeState.Waiting)
            {
                return peerKey;
            }

            GroupMemberStatus[] changed = [activeMember with { Update = NoticeState.Waiting }];
            await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
            Replace(group.Id, changed);
            return peerKey;
        }

        if (group.IsAuthority && group.FindAny(peerKey) is { State: GroupMemberState.Removed, RemovedEpoch: >= 2 } removedMember)
        {
            if (removedMember.Removed == NoticeState.Waiting)
            {
                return peerKey;
            }

            GroupMemberStatus[] changed = [removedMember with { Removed = NoticeState.Waiting }];
            await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
            Replace(group.Id, changed);
            return peerKey;
        }

        return null;
    }

    private void AddHandoverProofsLocked(Group group, Ed25519PublicKey contactKey, List<PendingSend> pending)
    {
        foreach (GroupLeave proof in group.LeaveProofs.Where(static proof => proof.HasSuccessor).OrderBy(static proof => proof.Epoch))
        {
            if (!confirmedProofs.Contains((group.Id, contactKey, proof.NoticeId)))
            {
                pending.Add(new PendingSend(proof.NoticeId, proof));
            }
        }
    }

    private bool TryGetEntry(GroupId groupId, [NotNullWhen(true)] out Entry? entry)
    {
        lock (gate)
        {
            return groups.TryGetValue(groupId, out entry);
        }
    }

    private MemberJoin EarlyJoinOf(GroupRecord record, Ed25519PublicKey memberKey)
    {
        lock (gate)
        {
            return earlyJoins.Remove((record.GroupId, memberKey), out GroupJoin? join) && !memberKey.Equals(own.Keys.IdentityKey)
                ? JoinOf(record, join)
                : MemberJoin.NotYet;
        }
    }

    private void KeepEarly(Ed25519PublicKey senderKey, GroupJoin join)
    {
        lock (gate)
        {
            if (earlyJoins.Count < EarlyJoinLimit || earlyJoins.ContainsKey((join.GroupId, senderKey)))
            {
                earlyJoins[(join.GroupId, senderKey)] = join;
            }
        }
    }

    private bool IsAddedContact(Ed25519PublicKey key) => contacts.TryGet(key, out Contact? contact) && contact.IsAdded;

    private Group Replace(GroupId groupId, IReadOnlyCollection<GroupMemberStatus> changed)
    {
        lock (gate)
        {
            Entry entry = groups[groupId];
            Group updated = entry.Group.With(changed);
            groups[groupId] = entry with { Group = updated };
            return updated;
        }
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (!e.Contact.IsAdded || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        _ = Task.Run(JoinQuietlyAsync, CancellationToken.None);
    }

    private async Task JoinQuietlyAsync()
    {
        try
        {
            await JoinWhereReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            if (failureSink is { } sink)
            {
                sink.Report(FailureSource.Groups, exception);
                return;
            }

            JoinFailed(logger, Failures.TypeName(exception), null);
        }
    }

    private sealed record Entry(Group Group, GroupKey Key);
}
