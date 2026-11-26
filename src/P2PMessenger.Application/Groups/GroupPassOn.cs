using P2PMessenger.Application.Delivery;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Application.Groups;

internal sealed class GroupPassOn : IDisposable
{
    private const int EarlySummaryLimit = 256;
    private const int PendingOwnAcknowledgementLimit = 4096;
    private const int SummaryBurst = 4;
    private const int DelayedTriggerCap = 64;
    private static readonly TimeSpan MaxRefillInterval = TimeSpan.FromSeconds(2);

    private readonly GroupBook groups;
    private readonly GroupChats groupChats;
    private readonly DeliveryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan summaryRefillInterval;
    private readonly ITimer summaryTimer;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, PeerSession> sessions = [];
    private readonly Dictionary<(Ed25519PublicKey Peer, GroupId Group), EarlySummary> earlySummaries = [];
    private readonly Dictionary<Ed25519PublicKey, List<GroupAcknowledgement>> pendingOwnAcknowledgements = [];
    private int disposed;

    public GroupPassOn(GroupBook groups, GroupChats groupChats, DeliveryOptions options, TimeProvider timeProvider)
    {
        this.groups = groups;
        this.groupChats = groupChats;
        this.options = options;
        this.timeProvider = timeProvider;
        summaryRefillInterval = options.ResendAfter < MaxRefillInterval ? options.ResendAfter : MaxRefillInterval;
        summaryTimer = timeProvider.CreateTimer(
            static state => ((GroupPassOn)state!).OnSummaryTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        groups.Changed += OnGroupChanged;
        groupChats.MessageArrived += OnMessageArrived;
        groupChats.AcknowledgementArrived += OnAcknowledgementArrived;
    }

    public event EventHandler<Ed25519PublicKey>? DeliveryNeeded;

    public void OnConnected(ContactConnection connection, Func<Ed25519PublicKey, ContactConnection, bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(isCurrent);

        Ed25519PublicKey peerKey = connection.Contact.IdentityKey;
        bool hasWork;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0 || connection.IsClosed || !isCurrent(peerKey, connection))
            {
                return;
            }

            PeerSession session = new(connection);
            sessions[peerKey] = session;
            foreach (Group group in groups.All)
            {
                if (group.State == GroupState.Created && IsOtherMember(group, peerKey))
                {
                    session.EnqueueInitialSummary(group.Id);
                }
            }

            hasWork = session.PendingSummaries.Count > 0;
        }

        if (hasWork)
        {
            DeliveryNeeded?.Invoke(this, peerKey);
        }
    }

    public void OnDisconnected(ContactConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Ed25519PublicKey peerKey = connection.Contact.IdentityKey;
        lock (gate)
        {
            if (sessions.TryGetValue(peerKey, out PeerSession? session) && ReferenceEquals(session.Connection, connection))
            {
                sessions.Remove(peerKey);
                ArmTimerLocked(timeProvider.GetTimestamp());
            }

            foreach ((Ed25519PublicKey Peer, GroupId Group) key in earlySummaries.Where(entry => ReferenceEquals(entry.Value.Connection, connection)).Select(static entry => entry.Key).ToList())
            {
                earlySummaries.Remove(key);
            }
        }
    }

    public void ReceiveSummary(ContactConnection connection, GroupSummary summary)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(summary);

        if (Volatile.Read(ref disposed) != 0 || connection.IsClosed)
        {
            return;
        }

        Ed25519PublicKey senderKey = connection.Contact.IdentityKey;
        if (!groups.TryGet(summary.GroupId, out Group? group)
            || !IsOtherMember(group, senderKey)
            || summary.Epoch != group.Record.Epoch
            || group.State == GroupState.ListsDiffer)
        {
            return;
        }

        if (group.State != GroupState.Created)
        {
            lock (gate)
            {
                if (Volatile.Read(ref disposed) != 0 || connection.IsClosed)
                {
                    return;
                }

                (Ed25519PublicKey, GroupId) key = (senderKey, group.Id);
                if (earlySummaries.Count < EarlySummaryLimit || earlySummaries.ContainsKey(key))
                {
                    earlySummaries[key] = new EarlySummary(connection, summary);
                }
            }

            return;
        }

        if (ApplySummary(connection, group, summary))
        {
            DeliveryNeeded?.Invoke(this, senderKey);
        }
    }

    public void MarkPeerHas(ContactConnection connection, MessageId id)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(id);

        if (Volatile.Read(ref disposed) != 0 || !groupChats.TryFindMessage(id, out GroupChatMessage? message))
        {
            return;
        }

        Ed25519PublicKey peerKey = connection.Contact.IdentityKey;
        IReadOnlyList<RelayedGroupAcknowledgement> relayedAcks = groupChats.RelayedAcknowledgementsIn(message.GroupId);
        bool notify = false;
        lock (gate)
        {
            if (sessions.TryGetValue(peerKey, out PeerSession? session) && ReferenceEquals(session.Connection, connection))
            {
                session.MarkPeerHas(message.GroupId, message.Id, message.AuthorKey, message.Sequence);
                foreach (RelayedGroupAcknowledgement relayed in relayedAcks)
                {
                    if (relayed.MessageId.Equals(id)
                        && !relayed.AuthorKey.Equals(peerKey)
                        && !relayed.RecipientKey.Equals(peerKey)
                        && session.EnqueueAcknowledgement(relayed.NoticeId))
                    {
                        notify = true;
                    }
                }
            }
        }

        if (notify)
        {
            DeliveryNeeded?.Invoke(this, peerKey);
        }
    }

    public void MarkRelayedAcknowledgementConfirmed(ContactConnection connection, MessageId noticeId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(noticeId);

        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Ed25519PublicKey peerKey = connection.Contact.IdentityKey;
        lock (gate)
        {
            if (sessions.TryGetValue(peerKey, out PeerSession? session) && ReferenceEquals(session.Connection, connection))
            {
                session.MarkPeerHasAcknowledgement(noticeId);
            }
        }
    }

    public IReadOnlyList<GroupSummary> TakeSummaries(Ed25519PublicKey peerKey, ContactConnection connection)
    {
        List<GroupId> groupIds = [];
        lock (gate)
        {
            if (!sessions.TryGetValue(peerKey, out PeerSession? session) || !ReferenceEquals(session.Connection, connection))
            {
                return [];
            }

            while (session.PendingSummaries.TryDequeue(out GroupId? groupId))
            {
                session.PendingSummarySet.Remove(groupId);
                groupIds.Add(groupId);
            }
        }

        List<GroupSummary> summaries = [];
        foreach (GroupId groupId in groupIds)
        {
            if (groups.TryGet(groupId, out Group? group) && group.State == GroupState.Created && IsOtherMember(group, peerKey))
            {
                summaries.Add(groupChats.SummaryFor(group));
            }
        }

        return summaries;
    }

    public bool TryDequeueMessage(Ed25519PublicKey peerKey, ContactConnection connection, out PassOnItem item)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(peerKey, out PeerSession? session) || !ReferenceEquals(session.Connection, connection))
            {
                item = default;
                return false;
            }

            while (session.PendingMessages.TryDequeue(out PassOnItem candidate))
            {
                if (session.PendingMessageSet.Remove(candidate.Id) && !session.PeerHolds(candidate.GroupId, candidate.AuthorKey, candidate.Sequence))
                {
                    item = candidate;
                    return true;
                }
            }

            item = default;
            return false;
        }
    }

    public IReadOnlyList<MessageId> PendingAcknowledgementsFor(Ed25519PublicKey peerKey, ContactConnection connection)
    {
        lock (gate)
        {
            return sessions.TryGetValue(peerKey, out PeerSession? session) && ReferenceEquals(session.Connection, connection)
                ? session.SnapshotPendingAcknowledgements()
                : [];
        }
    }

    public void ForgetAcknowledgement(MessageId noticeId)
    {
        ArgumentNullException.ThrowIfNull(noticeId);

        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            foreach (PeerSession session in sessions.Values)
            {
                session.RemovePendingAcknowledgement(noticeId);
            }
        }
    }

    public void QueueOwnAcknowledgement(Ed25519PublicKey peerKey, GroupAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(peerKey);
        ArgumentNullException.ThrowIfNull(acknowledgement);

        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        bool notify = false;
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            if (!pendingOwnAcknowledgements.TryGetValue(peerKey, out List<GroupAcknowledgement>? pending))
            {
                if (TotalPendingOwnAcknowledgementsLocked() >= PendingOwnAcknowledgementLimit)
                {
                    return;
                }

                pending = [];
                pendingOwnAcknowledgements.Add(peerKey, pending);
            }

            if (!pending.Exists(existing => existing.NoticeId.Equals(acknowledgement.NoticeId))
                && TotalPendingOwnAcknowledgementsLocked() < PendingOwnAcknowledgementLimit)
            {
                pending.Add(acknowledgement);
                notify = true;
            }
        }

        if (notify)
        {
            DeliveryNeeded?.Invoke(this, peerKey);
        }
    }

    public bool ConfirmOwnAcknowledgement(Ed25519PublicKey peerKey, MessageId noticeId)
    {
        ArgumentNullException.ThrowIfNull(peerKey);
        ArgumentNullException.ThrowIfNull(noticeId);

        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        lock (gate)
        {
            if (!pendingOwnAcknowledgements.TryGetValue(peerKey, out List<GroupAcknowledgement>? pending))
            {
                return false;
            }

            int removed = pending.RemoveAll(existing => existing.NoticeId.Equals(noticeId));
            if (pending.Count == 0)
            {
                pendingOwnAcknowledgements.Remove(peerKey);
            }

            return removed > 0;
        }
    }

    public IReadOnlyList<GroupAcknowledgement> PendingOwnAcknowledgementsFor(Ed25519PublicKey peerKey)
    {
        ArgumentNullException.ThrowIfNull(peerKey);

        if (Volatile.Read(ref disposed) != 0)
        {
            return [];
        }

        GroupAcknowledgement[] snapshot;
        lock (gate)
        {
            if (!pendingOwnAcknowledgements.TryGetValue(peerKey, out List<GroupAcknowledgement>? pending) || pending.Count == 0)
            {
                return [];
            }

            snapshot = [.. pending];
        }

        List<GroupAcknowledgement> valid = new(snapshot.Length);
        HashSet<MessageId>? stale = null;
        foreach (GroupAcknowledgement acknowledgement in snapshot)
        {
            if (groups.TryGet(acknowledgement.GroupId, out Group? group)
                && group.State == GroupState.Created
                && IsOtherMember(group, peerKey))
            {
                valid.Add(acknowledgement);
            }
            else
            {
                stale ??= [];
                stale.Add(acknowledgement.NoticeId);
            }
        }

        if (stale is { Count: > 0 })
        {
            lock (gate)
            {
                if (pendingOwnAcknowledgements.TryGetValue(peerKey, out List<GroupAcknowledgement>? pending))
                {
                    pending.RemoveAll(existing => stale.Contains(existing.NoticeId));
                    if (pending.Count == 0)
                    {
                        pendingOwnAcknowledgements.Remove(peerKey);
                    }
                }
            }
        }

        return valid;
    }

    public bool HasPendingMessages(Ed25519PublicKey peerKey, ContactConnection connection)
    {
        lock (gate)
        {
            return sessions.TryGetValue(peerKey, out PeerSession? session)
                && ReferenceEquals(session.Connection, connection)
                && session.PendingMessageSet.Count > 0;
        }
    }

    public IReadOnlyList<Ed25519PublicKey> ContactsWithPendingWork()
    {
        lock (gate)
        {
            return
            [
                .. sessions
                    .Where(static pair => !pair.Value.Connection.IsClosed
                        && (pair.Value.PendingSummaries.Count > 0 || pair.Value.PendingMessageSet.Count > 0 || pair.Value.PendingAcknowledgementSet.Count > 0))
                    .Select(static pair => pair.Key)
                    .Concat(pendingOwnAcknowledgements.Keys)
                    .Distinct(),
            ];
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        groups.Changed -= OnGroupChanged;
        groupChats.MessageArrived -= OnMessageArrived;
        groupChats.AcknowledgementArrived -= OnAcknowledgementArrived;
        summaryTimer.Dispose();
        lock (gate)
        {
            sessions.Clear();
            earlySummaries.Clear();
            pendingOwnAcknowledgements.Clear();
        }
    }

    private static bool IsOtherMember(Group group, Ed25519PublicKey key) =>
        !key.Equals(group.OwnKey) && group.Find(key) is not null;

    private int TotalPendingOwnAcknowledgementsLocked() =>
        pendingOwnAcknowledgements.Values.Sum(static list => list.Count);

    private bool ApplySummary(ContactConnection connection, Group group, GroupSummary summary)
    {
        Ed25519PublicKey senderKey = connection.Contact.IdentityKey;
        long groupVersion = groupChats.VersionOf(group.Id);
        List<(Ed25519PublicKey AuthorKey, SequenceRange Range)> newlyAdvertised = [];
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0 || connection.IsClosed)
            {
                return false;
            }

            if (!sessions.TryGetValue(senderKey, out PeerSession? session) || !ReferenceEquals(session.Connection, connection))
            {
                return false;
            }

            long now = timeProvider.GetTimestamp();
            if (!session.TryAcceptSummary(group.Id, summary, groupVersion, now, timeProvider, options.ResendAfter, summaryRefillInterval))
            {
                return false;
            }

            foreach (GroupSummaryAuthor author in summary.Authors)
            {
                if (group.Find(author.AuthorKey) is null)
                {
                    continue;
                }

                foreach (SequenceRange range in author.Ranges)
                {
                    if (!session.PeerHoldsRange(group.Id, author.AuthorKey, range))
                    {
                        newlyAdvertised.Add((author.AuthorKey, range));
                    }
                }
            }

            session.RecordSummary(group, summary);
        }

        IReadOnlyList<GroupChatMessage> missing = groupChats.MissingFrom(group, senderKey, summary);
        IReadOnlyList<RelayedGroupAcknowledgement> relayedAcks = groupChats.RelayedAcknowledgementsIn(group.Id);
        bool requestMissing = newlyAdvertised.Count > 0 && groupChats.HasUnheld(group, newlyAdvertised);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0 || connection.IsClosed)
            {
                return false;
            }

            if (!sessions.TryGetValue(senderKey, out PeerSession? session) || !ReferenceEquals(session.Connection, connection))
            {
                return false;
            }

            bool queued = false;
            foreach (GroupChatMessage message in missing)
            {
                if (!session.PeerHolds(group.Id, message.AuthorKey, message.Sequence)
                    && session.EnqueueMessage(new PassOnItem(group.Id, message.Id, message.AuthorKey, message.Sequence)))
                {
                    queued = true;
                }
            }

            foreach (RelayedGroupAcknowledgement relayed in relayedAcks)
            {
                if (!relayed.AuthorKey.Equals(senderKey)
                    && !relayed.RecipientKey.Equals(senderKey)
                    && session.PeerHolds(group.Id, relayed.AuthorKey, relayed.Sequence)
                    && session.EnqueueAcknowledgement(relayed.NoticeId))
                {
                    queued = true;
                }
            }

            if (requestMissing && session.EnqueueSummaryUpdate(group.Id))
            {
                queued = true;
            }

            return queued;
        }
    }

    private void OnGroupChanged(object? sender, GroupChangedEventArgs e)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Group group = e.Group;
        if (group.State == GroupState.ListsDiffer)
        {
            lock (gate)
            {
                foreach ((Ed25519PublicKey Peer, GroupId Group) key in earlySummaries.Keys.Where(entry => entry.Group.Equals(group.Id)).ToList())
                {
                    earlySummaries.Remove(key);
                }

                foreach (Ed25519PublicKey peerKey in pendingOwnAcknowledgements.Keys.ToList())
                {
                    List<GroupAcknowledgement> pending = pendingOwnAcknowledgements[peerKey];
                    pending.RemoveAll(acknowledgement => acknowledgement.GroupId.Equals(group.Id));
                    if (pending.Count == 0)
                    {
                        pendingOwnAcknowledgements.Remove(peerKey);
                    }
                }
            }

            return;
        }

        if (group.State != GroupState.Created)
        {
            return;
        }

        List<Ed25519PublicKey> notify = [];
        List<(ContactConnection Connection, GroupSummary Summary)> pendingEarly = [];
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            foreach (GroupMemberStatus member in group.Others)
            {
                Ed25519PublicKey memberKey = member.IdentityKey;
                if (!sessions.TryGetValue(memberKey, out PeerSession? session) || session.Connection.IsClosed)
                {
                    continue;
                }

                if (session.EnqueueInitialSummary(group.Id))
                {
                    notify.Add(memberKey);
                }

                if (earlySummaries.Remove((memberKey, group.Id), out EarlySummary early)
                    && ReferenceEquals(early.Connection, session.Connection)
                    && early.Summary.Epoch == group.Record.Epoch)
                {
                    pendingEarly.Add((early.Connection, early.Summary));
                }
            }
        }

        foreach ((ContactConnection connection, GroupSummary summary) in pendingEarly)
        {
            if (ApplySummary(connection, group, summary))
            {
                notify.Add(connection.Contact.IdentityKey);
            }
        }

        foreach (Ed25519PublicKey memberKey in notify.Distinct())
        {
            DeliveryNeeded?.Invoke(this, memberKey);
        }
    }

    private void OnMessageArrived(object? sender, GroupMessageArrivedEventArgs e)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        GroupChatMessage message = e.Message;
        if (!groups.TryGet(message.GroupId, out Group? group)
            || group.State != GroupState.Created
            || message.Envelope.Epoch != group.Record.Epoch)
        {
            return;
        }

        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            if (sessions.TryGetValue(e.SenderKey, out PeerSession? senderSession))
            {
                senderSession.MarkPeerHas(group.Id, message.Id, message.AuthorKey, message.Sequence);
            }

            long now = timeProvider.GetTimestamp();
            bool scheduled = false;
            foreach (GroupMemberStatus member in group.Others)
            {
                Ed25519PublicKey memberKey = member.IdentityKey;
                if (memberKey.Equals(e.SenderKey)
                    || memberKey.Equals(message.AuthorKey)
                    || !sessions.TryGetValue(memberKey, out PeerSession? session)
                    || session.Connection.IsClosed
                    || session.PeerHolds(group.Id, message.AuthorKey, message.Sequence))
                {
                    continue;
                }

                session.ScheduleDelayedSummary(group.Id, message.AuthorKey, message.Sequence, now);
                scheduled = true;
            }

            if (scheduled)
            {
                ArmTimerLocked(now);
            }
        }
    }

    private void OnAcknowledgementArrived(object? sender, GroupAcknowledgementArrivedEventArgs e)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        RelayedGroupAcknowledgement relayed = e.Relayed;
        if (!groups.TryGet(relayed.GroupId, out Group? group) || group.State != GroupState.Created)
        {
            return;
        }

        List<Ed25519PublicKey> notify = [];
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            if (sessions.TryGetValue(e.SenderKey, out PeerSession? senderSession))
            {
                senderSession.MarkPeerHasAcknowledgement(relayed.NoticeId);
            }

            if (sessions.TryGetValue(relayed.RecipientKey, out PeerSession? recipientSession))
            {
                recipientSession.MarkPeerHas(relayed.GroupId, relayed.MessageId, relayed.AuthorKey, relayed.Sequence);
            }

            if (sessions.TryGetValue(relayed.AuthorKey, out PeerSession? authorSession) && !authorSession.Connection.IsClosed)
            {
                notify.Add(relayed.AuthorKey);
            }

            foreach (GroupMemberStatus member in group.Others)
            {
                Ed25519PublicKey memberKey = member.IdentityKey;
                if (memberKey.Equals(e.SenderKey)
                    || memberKey.Equals(relayed.AuthorKey)
                    || memberKey.Equals(relayed.RecipientKey)
                    || !sessions.TryGetValue(memberKey, out PeerSession? peerSession)
                    || peerSession.Connection.IsClosed
                    || !peerSession.PeerHolds(relayed.GroupId, relayed.AuthorKey, relayed.Sequence))
                {
                    continue;
                }

                if (peerSession.EnqueueAcknowledgement(relayed.NoticeId))
                {
                    notify.Add(memberKey);
                }
            }
        }

        foreach (Ed25519PublicKey peerKey in notify.Distinct())
        {
            DeliveryNeeded?.Invoke(this, peerKey);
        }
    }

    private void OnSummaryTimer()
    {
        List<Ed25519PublicKey> notify = [];
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            long now = timeProvider.GetTimestamp();
            foreach ((Ed25519PublicKey peerKey, PeerSession session) in sessions)
            {
                if (session.Connection.IsClosed)
                {
                    continue;
                }

                if (session.PromoteDueSummaries(now, timeProvider, options.PassOnSummaryDelay))
                {
                    notify.Add(peerKey);
                }
            }

            ArmTimerLocked(now);
        }

        foreach (Ed25519PublicKey peerKey in notify)
        {
            DeliveryNeeded?.Invoke(this, peerKey);
        }
    }

    private void ArmTimerLocked(long now)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        TimeSpan? earliest = null;
        foreach (PeerSession session in sessions.Values)
        {
            if (session.Connection.IsClosed)
            {
                continue;
            }

            if (session.NextDelayedSummaryIn(now, timeProvider, options.PassOnSummaryDelay) is { } remaining
                && (earliest is null || remaining < earliest.Value))
            {
                earliest = remaining;
            }
        }

        if (earliest is null)
        {
            summaryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan due = earliest.Value <= TimeSpan.Zero ? TimeSpan.Zero : earliest.Value;
        summaryTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private readonly record struct EarlySummary(ContactConnection Connection, GroupSummary Summary);

    private sealed class PeerSession
    {
        private readonly HashSet<GroupId> summarizedGroups = [];
        private readonly Dictionary<GroupId, PeerHeld> peerHeld = [];
        private readonly Dictionary<GroupId, SummaryGate> summaryGates = [];
        private readonly Dictionary<GroupId, DelayedSummary> delayedSummaries = [];
        private readonly List<MessageId> pendingAcknowledgements = [];
        private readonly HashSet<MessageId> peerHasAcknowledgements = [];

        public PeerSession(ContactConnection connection)
        {
            Connection = connection;
        }

        public ContactConnection Connection { get; }

        public Queue<GroupId> PendingSummaries { get; } = [];

        public HashSet<GroupId> PendingSummarySet { get; } = [];

        public Queue<PassOnItem> PendingMessages { get; } = [];

        public HashSet<MessageId> PendingMessageSet { get; } = [];

        public HashSet<MessageId> PendingAcknowledgementSet { get; } = [];

        public bool EnqueueInitialSummary(GroupId groupId) =>
            summarizedGroups.Add(groupId) && EnqueueSummaryUpdate(groupId);

        public bool EnqueueSummaryUpdate(GroupId groupId)
        {
            if (!PendingSummarySet.Add(groupId))
            {
                return false;
            }

            PendingSummaries.Enqueue(groupId);
            return true;
        }

        public bool EnqueueMessage(PassOnItem item)
        {
            if (!PendingMessageSet.Add(item.Id))
            {
                return false;
            }

            PendingMessages.Enqueue(item);
            return true;
        }

        public bool EnqueueAcknowledgement(MessageId noticeId)
        {
            if (peerHasAcknowledgements.Contains(noticeId) || !PendingAcknowledgementSet.Add(noticeId))
            {
                return false;
            }

            pendingAcknowledgements.Add(noticeId);
            return true;
        }

        public void MarkPeerHasAcknowledgement(MessageId noticeId)
        {
            RemovePendingAcknowledgement(noticeId);
            peerHasAcknowledgements.Add(noticeId);
        }

        public void RemovePendingAcknowledgement(MessageId noticeId)
        {
            if (PendingAcknowledgementSet.Remove(noticeId))
            {
                pendingAcknowledgements.Remove(noticeId);
            }
        }

        public IReadOnlyList<MessageId> SnapshotPendingAcknowledgements() => [.. pendingAcknowledgements];

        public bool TryAcceptSummary(
            GroupId groupId,
            GroupSummary summary,
            long groupVersion,
            long now,
            TimeProvider timeProvider,
            TimeSpan resendAfter,
            TimeSpan refillInterval)
        {
            if (!summaryGates.TryGetValue(groupId, out SummaryGate? summaryGate))
            {
                summaryGate = new SummaryGate(now);
                summaryGates.Add(groupId, summaryGate);
            }

            return summaryGate.TryAccept(summary, groupVersion, now, timeProvider, resendAfter, refillInterval);
        }

        public void RecordSummary(Group group, GroupSummary summary)
        {
            if (!peerHeld.TryGetValue(group.Id, out PeerHeld? held))
            {
                held = new PeerHeld();
                peerHeld.Add(group.Id, held);
            }

            held.Merge(group, summary);
        }

        public void MarkPeerHas(GroupId groupId, MessageId id, Ed25519PublicKey authorKey, long sequence)
        {
            PendingMessageSet.Remove(id);
            if (!peerHeld.TryGetValue(groupId, out PeerHeld? held))
            {
                held = new PeerHeld();
                peerHeld.Add(groupId, held);
            }

            held.Add(authorKey, sequence);
        }

        public bool PeerHolds(GroupId groupId, Ed25519PublicKey authorKey, long sequence) =>
            peerHeld.TryGetValue(groupId, out PeerHeld? held) && held.Contains(authorKey, sequence);

        public bool PeerHoldsRange(GroupId groupId, Ed25519PublicKey authorKey, SequenceRange range) =>
            peerHeld.TryGetValue(groupId, out PeerHeld? held) && held.Covers(authorKey, range);

        public void ScheduleDelayedSummary(GroupId groupId, Ed25519PublicKey authorKey, long sequence, long now)
        {
            if (!delayedSummaries.TryGetValue(groupId, out DelayedSummary? delayed))
            {
                delayed = new DelayedSummary(now);
                delayedSummaries.Add(groupId, delayed);
            }

            delayed.AddTrigger(authorKey, sequence);
        }

        public bool PromoteDueSummaries(long now, TimeProvider timeProvider, TimeSpan delay)
        {
            if (delayedSummaries.Count == 0)
            {
                return false;
            }

            List<GroupId> due = [];
            foreach ((GroupId groupId, DelayedSummary delayed) in delayedSummaries)
            {
                if (timeProvider.GetElapsedTime(delayed.ScheduledAt, now) >= delay)
                {
                    due.Add(groupId);
                }
            }

            bool enqueued = false;
            foreach (GroupId groupId in due)
            {
                DelayedSummary delayed = delayedSummaries[groupId];
                delayedSummaries.Remove(groupId);
                if (delayed.NeedsSend(this, groupId) && EnqueueSummaryUpdate(groupId))
                {
                    enqueued = true;
                }
            }

            return enqueued;
        }

        public TimeSpan? NextDelayedSummaryIn(long now, TimeProvider timeProvider, TimeSpan delay)
        {
            TimeSpan? earliest = null;
            foreach (DelayedSummary delayed in delayedSummaries.Values)
            {
                TimeSpan remaining = delay - timeProvider.GetElapsedTime(delayed.ScheduledAt, now);
                if (earliest is null || remaining < earliest.Value)
                {
                    earliest = remaining;
                }
            }

            return earliest;
        }
    }

    private sealed class DelayedSummary
    {
        private readonly List<(Ed25519PublicKey AuthorKey, long Sequence)> triggers = [];
        private bool alwaysSend;

        public DelayedSummary(long scheduledAt)
        {
            ScheduledAt = scheduledAt;
        }

        public long ScheduledAt { get; }

        public void AddTrigger(Ed25519PublicKey authorKey, long sequence)
        {
            if (alwaysSend)
            {
                return;
            }

            if (triggers.Count >= DelayedTriggerCap)
            {
                alwaysSend = true;
                triggers.Clear();
                return;
            }

            triggers.Add((authorKey, sequence));
        }

        public bool NeedsSend(PeerSession session, GroupId groupId) =>
            alwaysSend || triggers.Exists(trigger => !session.PeerHolds(groupId, trigger.AuthorKey, trigger.Sequence));
    }

    private sealed class SummaryGate
    {
        private int tokens = SummaryBurst;
        private long lastRefillAt;
        private GroupSummary? lastSummary;
        private long lastGroupVersion = -1;
        private long lastProcessedAt;

        public SummaryGate(long now)
        {
            lastRefillAt = now;
        }

        public bool TryAccept(
            GroupSummary summary,
            long groupVersion,
            long now,
            TimeProvider timeProvider,
            TimeSpan resendAfter,
            TimeSpan refillInterval)
        {
            if (lastSummary is not null
                && lastGroupVersion == groupVersion
                && lastSummary.Equals(summary)
                && timeProvider.GetElapsedTime(lastProcessedAt, now) < resendAfter)
            {
                return false;
            }

            TimeSpan sinceRefill = timeProvider.GetElapsedTime(lastRefillAt, now);
            if (sinceRefill >= refillInterval && tokens < SummaryBurst)
            {
                int added = (int)Math.Min(SummaryBurst - tokens, Math.Floor(sinceRefill.TotalMilliseconds / refillInterval.TotalMilliseconds));
                if (added > 0)
                {
                    tokens += added;
                    lastRefillAt = now;
                }
            }

            if (tokens <= 0)
            {
                return false;
            }

            tokens--;
            lastSummary = summary;
            lastGroupVersion = groupVersion;
            lastProcessedAt = now;
            return true;
        }
    }

    private sealed class PeerHeld
    {
        private readonly Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor = [];

        public void Merge(Group group, GroupSummary summary)
        {
            foreach (GroupSummaryAuthor author in summary.Authors)
            {
                if (group.Find(author.AuthorKey) is null)
                {
                    continue;
                }

                AddRanges(author.AuthorKey, author.Ranges);
            }
        }

        public void Add(Ed25519PublicKey authorKey, long sequence) =>
            AddRanges(authorKey, [new SequenceRange(sequence, sequence)]);

        public bool Contains(Ed25519PublicKey authorKey, long sequence) =>
            Covers(authorKey, new SequenceRange(sequence, sequence));

        public bool Covers(Ed25519PublicKey authorKey, SequenceRange target)
        {
            if (!byAuthor.TryGetValue(authorKey, out List<SequenceRange>? ranges))
            {
                return false;
            }

            int low = 0;
            int high = ranges.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                SequenceRange range = ranges[middle];
                if (target.First < range.First)
                {
                    high = middle - 1;
                }
                else if (target.First > range.Last)
                {
                    low = middle + 1;
                }
                else
                {
                    return range.Covers(target);
                }
            }

            return false;
        }

        private void AddRanges(Ed25519PublicKey authorKey, IReadOnlyList<SequenceRange> incoming)
        {
            if (!byAuthor.TryGetValue(authorKey, out List<SequenceRange>? ranges))
            {
                byAuthor.Add(authorKey, SequenceRange.Merge(incoming));
                return;
            }

            ranges.AddRange(incoming);
            byAuthor[authorKey] = SequenceRange.Merge(ranges);
        }
    }
}
