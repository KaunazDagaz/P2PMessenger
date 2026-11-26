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
            if (!group.Record.HasMember(own.Keys.IdentityKey) || !groups.TryAdd(group.Record.GroupId, new Entry(new Group(group.Record, group.Members, own.Keys.IdentityKey), group.Key)))
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
                if (group.YouJoined || group.Others.Any(member => !IsAddedContact(member.IdentityKey)))
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
        Group? updated;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGet(join.GroupId, out Group? group))
            {
                KeepEarly(senderKey, join);
                return false;
            }

            if (group.Find(senderKey) is not { } member || member.IdentityKey.Equals(own.Keys.IdentityKey))
            {
                return true;
            }

            MemberJoin joined = JoinOf(group.Record, join);
            if (member.Joined == joined || member.Joined == MemberJoin.JoinedAnotherList)
            {
                return true;
            }

            GroupMemberStatus[] changed = [member with { Joined = joined }];
            await store.UpdateMembersAsync(group.Id, changed, cancellationToken).ConfigureAwait(false);
            updated = Replace(group.Id, changed);
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        return true;
    }

    internal async Task<bool> MarkNoticeReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        Group? updated = null;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (Group group in All)
            {
                if (group.Find(contactKey) is not { } member || contactKey.Equals(own.Keys.IdentityKey))
                {
                    continue;
                }

                GroupMemberStatus received = member;
                if (member.Invitation == NoticeState.Waiting && group.YouCreated && GroupNotices.InvitationId(group.Record).Equals(id))
                {
                    received = received with { Invitation = NoticeState.Received };
                }

                if (member.OwnJoin == NoticeState.Waiting && GroupNotices.JoinId(group.Record.Hash, own.Keys.IdentityKey).Equals(id))
                {
                    received = received with { OwnJoin = NoticeState.Received };
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

        if (updated is null)
        {
            return false;
        }

        Changed?.Invoke(this, new GroupChangedEventArgs(updated));
        return true;
    }

    internal IReadOnlyList<PendingSend> PendingNoticesFor(Ed25519PublicKey contactKey)
    {
        List<PendingSend> pending = [];
        lock (gate)
        {
            foreach (Entry entry in groups.Values)
            {
                Group group = entry.Group;
                if (group.Find(contactKey) is not { } member || contactKey.Equals(own.Keys.IdentityKey))
                {
                    continue;
                }

                if (group.YouCreated && member.Invitation == NoticeState.Waiting)
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
            return
            [
                .. groups.Values
                    .SelectMany(entry => entry.Group.Others.Where(member =>
                        member.OwnJoin == NoticeState.Waiting || (entry.Group.YouCreated && member.Invitation == NoticeState.Waiting)))
                    .Select(static member => member.IdentityKey)
                    .Distinct(),
            ];
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

    private static MemberJoin JoinOf(GroupRecord record, GroupJoin join) =>
        join.Epoch == record.Epoch && join.RecordHash.SequenceEqual(record.Hash) ? MemberJoin.Joined : MemberJoin.JoinedAnotherList;

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
