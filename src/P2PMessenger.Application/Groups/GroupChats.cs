using System.Diagnostics.CodeAnalysis;
using System.Text;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

public sealed class GroupChats : IDisposable
{
    private readonly GroupBook groups;
    private readonly OwnIdentity own;
    private readonly IGroupStore store;
    private readonly ISeenMessageIds seen;
    private readonly TimeProvider timeProvider;
    private readonly Func<Ed25519PublicKey, CancellationToken, Task<IDisposable>> enterDelivery;
    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<GroupId, Conversation> conversations = [];
    private readonly Dictionary<MessageId, GroupChatMessage> messagesById = [];

    internal GroupChats(
        GroupBook groups,
        OwnIdentity own,
        IGroupStore store,
        ISeenMessageIds seen,
        TimeProvider timeProvider,
        IEnumerable<GroupChatMessage> loaded,
        IReadOnlyDictionary<GroupId, IReadOnlyList<AuthorSequenceRange>> deleted,
        Func<Ed25519PublicKey, CancellationToken, Task<IDisposable>> enterDelivery)
    {
        this.groups = groups;
        this.own = own;
        this.store = store;
        this.seen = seen;
        this.timeProvider = timeProvider;
        this.enterDelivery = enterDelivery;

        foreach (GroupChatMessage message in loaded.OrderBy(static message => message.RecordedAt))
        {
            ConversationOf(message.GroupId).Insert(message, own.Keys.IdentityKey);
            messagesById[message.Id] = message;
        }

        foreach ((GroupId groupId, IReadOnlyList<AuthorSequenceRange> ranges) in deleted)
        {
            ConversationOf(groupId).KeepDeleted(DeletedRanges.From(ranges), own.Keys.IdentityKey);
        }
    }

    public event EventHandler<GroupChatChangedEventArgs>? Changed;

    internal event EventHandler<Ed25519PublicKey>? DeliveryNeeded;

    internal event EventHandler<GroupMessageArrivedEventArgs>? MessageArrived;

    public IReadOnlyList<GroupChatMessage> MessagesIn(GroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        lock (gate)
        {
            return conversations.TryGetValue(groupId, out Conversation? conversation) ? [.. conversation.Messages] : [];
        }
    }

    public int UnreadCount(GroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        lock (gate)
        {
            return conversations.TryGetValue(groupId, out Conversation? conversation) ? conversation.Unread : 0;
        }
    }

    public void MarkRead(GroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        lock (gate)
        {
            if (!conversations.TryGetValue(groupId, out Conversation? conversation) || conversation.Unread == 0)
            {
                return;
            }

            conversation.Unread = 0;
        }

        Changed?.Invoke(this, new GroupChatChangedEventArgs(groupId, null, null));
    }

    public void Dispose() => writing.Dispose();

    internal async Task<int> DeleteChatAsync(GroupId groupId, CancellationToken cancellationToken)
    {
        if (!groups.TryGet(groupId, out Group? group))
        {
            return 0;
        }

        int removed;
        IReadOnlyList<IDisposable> turns = await EnterDeliveriesAsync(group, cancellationToken).ConfigureAwait(false);
        try
        {
            await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DeletedRanges updated;
                lock (gate)
                {
                    Conversation conversation = ConversationOf(groupId);
                    updated = conversation.Deleted.With(conversation.Messages.Select(static message => (message.AuthorKey, message.Sequence)));
                }

                removed = await store.DeleteChatAsync(groupId, updated.ToList(), cancellationToken).ConfigureAwait(false);
                lock (gate)
                {
                    Conversation conversation = ConversationOf(groupId);
                    foreach (GroupChatMessage message in conversation.Messages)
                    {
                        messagesById.Remove(message.Id);
                    }

                    conversation.Clear(updated);
                }
            }
            finally
            {
                writing.Release();
            }
        }
        finally
        {
            Leave(turns);
        }

        Changed?.Invoke(this, GroupChatChangedEventArgs.ForCleared(groupId));
        return removed;
    }

    internal async Task<bool> DeleteWaitingAsync(GroupId groupId, MessageId id, CancellationToken cancellationToken)
    {
        if (!groups.TryGet(groupId, out Group? group))
        {
            return false;
        }

        GroupChatMessage deleted;
        IReadOnlyList<IDisposable> turns = await EnterDeliveriesAsync(group, cancellationToken).ConfigureAwait(false);
        try
        {
            await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                GroupChatMessage? waiting;
                DeletedRanges updated;
                lock (gate)
                {
                    Conversation conversation = ConversationOf(groupId);
                    waiting = conversation.Messages.Find(message => message.Id.Equals(id) && message.CanBeDeleted);
                    updated = waiting is null ? conversation.Deleted : conversation.Deleted.With([(waiting.AuthorKey, waiting.Sequence)]);
                }

                if (waiting is null || !await store.DeleteWaitingAsync(groupId, id, updated.ToList(), cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                lock (gate)
                {
                    Conversation conversation = ConversationOf(groupId);
                    conversation.Remove(waiting);
                    messagesById.Remove(waiting.Id);
                    conversation.KeepDeleted(updated, own.Keys.IdentityKey);
                }

                deleted = waiting;
            }
            finally
            {
                writing.Release();
            }
        }
        finally
        {
            Leave(turns);
        }

        Changed?.Invoke(this, GroupChatChangedEventArgs.ForRemoved(deleted));
        return true;
    }

    internal async Task<GroupSendResult> SendAsync(GroupId groupId, string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new GroupSendResult(GroupSendOutcome.Empty, null);
        }

        if (!GroupText.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, text, out _))
        {
            bool tooLong = Encoding.UTF8.GetByteCount(text) > GroupText.MaxUtf8Length;
            return new GroupSendResult(tooLong ? GroupSendOutcome.TooLong : GroupSendOutcome.NotSendable, null);
        }

        if (!groups.TryGetKey(groupId, out Group? group, out GroupKey? key))
        {
            return new GroupSendResult(GroupSendOutcome.NotAGroup, null);
        }

        if (group.State != GroupState.Created)
        {
            return new GroupSendResult(GroupSendOutcome.NotCreatedYet, null);
        }

        GroupChatMessage message;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long sequence;
            lock (gate)
            {
                sequence = ConversationOf(groupId).LastSentSequence + 1;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (!GroupText.TryCreate(MessageId.Create(), sequence, now, text, out GroupText? groupText))
            {
                return new GroupSendResult(GroupSendOutcome.NotSendable, null);
            }

            GroupMessage envelope = GroupMessage.Seal(key, groupId, group.Record.Epoch, own.Keys, groupText);
            Dictionary<Ed25519PublicKey, DeliveryState> deliveries = group.Others.ToDictionary(static member => member.IdentityKey, static _ => DeliveryState.Waiting);
            message = new GroupChatMessage(envelope, groupText.Id, sequence, groupText.WrittenAt, groupText.Text, now, deliveries);
            if (!await store.AddMessageAsync(message, cancellationToken).ConfigureAwait(false))
            {
                return new GroupSendResult(GroupSendOutcome.NotSendable, null);
            }

            lock (gate)
            {
                ConversationOf(groupId).Insert(message, own.Keys.IdentityKey);
                messagesById[message.Id] = message;
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChatChangedEventArgs(groupId, message, null));
        foreach (Ed25519PublicKey member in message.Deliveries.Keys)
        {
            DeliveryNeeded?.Invoke(this, member);
        }

        return new GroupSendResult(GroupSendOutcome.Queued, message);
    }

    internal async Task<MessageId?> ReceiveAsync(Ed25519PublicKey senderKey, GroupMessage envelope, CancellationToken cancellationToken)
    {
        if (!groups.TryGetKey(envelope.GroupId, out Group? group, out GroupKey? key)
            || group.Find(senderKey) is null
            || group.Find(envelope.AuthorKey) is null
            || envelope.Epoch != group.Record.Epoch
            || !envelope.TryOpen(key, out GroupText? text))
        {
            return null;
        }

        GroupChatMessage message;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await seen.ContainsAsync(text.Id, cancellationToken).ConfigureAwait(false))
            {
                return text.Id;
            }

            lock (gate)
            {
                if (ConversationOf(group.Id).Deleted.Contains(envelope.AuthorKey, text.Sequence))
                {
                    return null;
                }
            }

            message = new GroupChatMessage(envelope, text.Id, text.Sequence, text.WrittenAt, text.Text, timeProvider.GetUtcNow(), new Dictionary<Ed25519PublicKey, DeliveryState>());
            if (envelope.AuthorKey.Equals(own.Keys.IdentityKey) || !await store.AddMessageAsync(message, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            lock (gate)
            {
                Conversation conversation = ConversationOf(group.Id);
                conversation.Insert(message, own.Keys.IdentityKey);
                messagesById[message.Id] = message;
                conversation.Unread++;
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChatChangedEventArgs(group.Id, message, null));
        MessageArrived?.Invoke(this, new GroupMessageArrivedEventArgs(senderKey, message));
        return text.Id;
    }

    internal async Task<bool> MarkReceivedAsync(Ed25519PublicKey memberKey, MessageId id, CancellationToken cancellationToken)
    {
        GroupChatMessage received;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GroupChatMessage? waiting;
            lock (gate)
            {
                waiting = messagesById.TryGetValue(id, out GroupChatMessage? candidate) && candidate.IsWaitingFor(memberKey) ? candidate : null;
            }

            if (waiting is null || !await store.MarkReceivedAsync(waiting.GroupId, memberKey, id, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            received = waiting.WithReceivedBy(memberKey);
            lock (gate)
            {
                ConversationOf(waiting.GroupId).Replace(waiting, received);
                messagesById[received.Id] = received;
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new GroupChatChangedEventArgs(received.GroupId, null, received));
        return true;
    }

    internal IReadOnlyList<PendingSend> WaitingFor(Ed25519PublicKey memberKey)
    {
        lock (gate)
        {
            return
            [
                .. conversations.Values
                    .SelectMany(static conversation => conversation.Messages)
                    .Where(message => message.IsWaitingFor(memberKey))
                    .OrderBy(static message => message.Sequence)
                    .Select(static message => new PendingSend(message.Id, message.Envelope)),
            ];
        }
    }

    internal bool IsWaitingFor(Ed25519PublicKey memberKey, MessageId id)
    {
        lock (gate)
        {
            return messagesById.TryGetValue(id, out GroupChatMessage? message) && message.IsWaitingFor(memberKey);
        }
    }

    internal IReadOnlyList<Ed25519PublicKey> MembersWithWaitingMessages()
    {
        lock (gate)
        {
            return
            [
                .. conversations.Values
                    .SelectMany(static conversation => conversation.Messages)
                    .SelectMany(static message => message.Deliveries.Where(static delivery => delivery.Value == DeliveryState.Waiting).Select(static delivery => delivery.Key))
                    .Distinct(),
            ];
        }
    }

    internal long VersionOf(GroupId groupId)
    {
        lock (gate)
        {
            return conversations.TryGetValue(groupId, out Conversation? conversation) ? conversation.Version : 0;
        }
    }

    internal GroupSummary SummaryFor(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Dictionary<Ed25519PublicKey, List<SequenceRange>> held = HeldByAuthor(group);
        return GroupSummary.Fit(group.Id, group.Record.Epoch, held.Select(static pair => (pair.Key, (IEnumerable<SequenceRange>)pair.Value)));
    }

    internal bool HasUnheld(Group group, IReadOnlyList<(Ed25519PublicKey AuthorKey, SequenceRange Range)> advertised)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(advertised);

        if (advertised.Count == 0)
        {
            return false;
        }

        Dictionary<Ed25519PublicKey, List<SequenceRange>> held = HeldByAuthor(group);
        foreach ((Ed25519PublicKey authorKey, SequenceRange range) in advertised)
        {
            if (group.Find(authorKey) is null)
            {
                continue;
            }

            if (!held.TryGetValue(authorKey, out List<SequenceRange>? ranges) || !Covers(ranges, range))
            {
                return true;
            }
        }

        return false;
    }

    internal IReadOnlyList<GroupChatMessage> MissingFrom(Group group, Ed25519PublicKey peerKey, GroupSummary peerSummary)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(peerKey);
        ArgumentNullException.ThrowIfNull(peerSummary);

        lock (gate)
        {
            if (!conversations.TryGetValue(group.Id, out Conversation? conversation))
            {
                return [];
            }

            return
            [
                .. conversation.Messages.Where(message =>
                    message.Envelope.Epoch == group.Record.Epoch
                    && group.Find(message.AuthorKey) is not null
                    && !message.AuthorKey.Equals(peerKey)
                    && !message.IsWaitingFor(peerKey)
                    && !peerSummary.Contains(message.AuthorKey, message.Sequence)),
            ];
        }
    }

    internal bool TryGetStoredEnvelope(GroupId groupId, MessageId id, [NotNullWhen(true)] out GroupMessage? envelope)
    {
        lock (gate)
        {
            if (messagesById.TryGetValue(id, out GroupChatMessage? found) && found.GroupId.Equals(groupId))
            {
                envelope = found.Envelope;
                return true;
            }
        }

        envelope = null;
        return false;
    }

    internal bool TryFindMessage(MessageId id, [NotNullWhen(true)] out GroupChatMessage? message)
    {
        lock (gate)
        {
            return messagesById.TryGetValue(id, out message);
        }
    }

    private static bool Covers(List<SequenceRange> merged, SequenceRange target)
    {
        int low = 0;
        int high = merged.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            SequenceRange range = merged[middle];
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

    private Dictionary<Ed25519PublicKey, List<SequenceRange>> HeldByAuthor(Group group)
    {
        lock (gate)
        {
            if (!conversations.TryGetValue(group.Id, out Conversation? conversation))
            {
                return [];
            }

            return conversation.HeldByAuthor(group);
        }
    }

    private static void Leave(IReadOnlyList<IDisposable> turns)
    {
        for (int index = turns.Count - 1; index >= 0; index--)
        {
            turns[index].Dispose();
        }
    }

    private async Task<IReadOnlyList<IDisposable>> EnterDeliveriesAsync(Group group, CancellationToken cancellationToken)
    {
        List<IDisposable> turns = [];
        try
        {
            foreach (Ed25519PublicKey memberKey in group.Others.Select(static member => member.IdentityKey).OrderBy(static key => key.Bytes.ToArray(), KeyOrder.Instance))
            {
                turns.Add(await enterDelivery(memberKey, cancellationToken).ConfigureAwait(false));
            }
        }
        catch
        {
            Leave(turns);
            throw;
        }

        return turns;
    }

    private Conversation ConversationOf(GroupId groupId)
    {
        if (!conversations.TryGetValue(groupId, out Conversation? conversation))
        {
            conversation = new Conversation();
            conversations.Add(groupId, conversation);
        }

        return conversation;
    }

    private sealed class Conversation
    {
        private int cachedEpoch;
        private long cachedVersion = -1;
        private Dictionary<Ed25519PublicKey, List<SequenceRange>> cachedHeld = [];

        public List<GroupChatMessage> Messages { get; } = [];

        public long LastSentSequence { get; private set; }

        public int Unread { get; set; }

        public long Version { get; private set; }

        public DeletedRanges Deleted { get; private set; } = DeletedRanges.Empty;

        public void KeepDeleted(DeletedRanges deleted, Ed25519PublicKey ownKey)
        {
            Deleted = deleted;
            LastSentSequence = Math.Max(LastSentSequence, deleted.HighestOf(ownKey));
            Version++;
        }

        public void Clear(DeletedRanges deleted)
        {
            Messages.Clear();
            Unread = 0;
            Deleted = deleted;
            Version++;
        }

        public void Insert(GroupChatMessage message, Ed25519PublicKey ownKey)
        {
            int index = Messages.Count;
            while (index > 0 && ComesAfter(Messages[index - 1], message))
            {
                index--;
            }

            Messages.Insert(index, message);
            if (message.AuthorKey.Equals(ownKey) && message.Sequence > LastSentSequence)
            {
                LastSentSequence = message.Sequence;
            }

            Version++;
        }

        public void Remove(GroupChatMessage message)
        {
            if (Messages.Remove(message))
            {
                Version++;
            }
        }

        public void Replace(GroupChatMessage old, GroupChatMessage replacement)
        {
            int index = Messages.IndexOf(old);
            if (index >= 0)
            {
                Messages[index] = replacement;
            }
        }

        public Dictionary<Ed25519PublicKey, List<SequenceRange>> HeldByAuthor(Group group)
        {
            int epoch = group.Record.Epoch;
            if (cachedVersion == Version && cachedEpoch == epoch)
            {
                return cachedHeld;
            }

            Dictionary<Ed25519PublicKey, List<SequenceRange>> collected = [];
            foreach (GroupChatMessage message in Messages)
            {
                if (message.Envelope.Epoch == epoch && group.Find(message.AuthorKey) is not null)
                {
                    AddHeld(collected, message.AuthorKey, new SequenceRange(message.Sequence, message.Sequence));
                }
            }

            foreach (AuthorSequenceRange deleted in Deleted.ToList())
            {
                if (group.Find(deleted.AuthorKey) is not null)
                {
                    AddHeld(collected, deleted.AuthorKey, deleted.Range);
                }
            }

            Dictionary<Ed25519PublicKey, List<SequenceRange>> merged = [];
            foreach ((Ed25519PublicKey authorKey, List<SequenceRange> ranges) in collected)
            {
                merged.Add(authorKey, SequenceRange.Merge(ranges));
            }

            cachedEpoch = epoch;
            cachedVersion = Version;
            cachedHeld = merged;
            return merged;
        }

        private static void AddHeld(Dictionary<Ed25519PublicKey, List<SequenceRange>> map, Ed25519PublicKey authorKey, SequenceRange range)
        {
            if (!map.TryGetValue(authorKey, out List<SequenceRange>? list))
            {
                list = [];
                map.Add(authorKey, list);
            }

            list.Add(range);
        }

        private static bool ComesAfter(GroupChatMessage earlier, GroupChatMessage message) =>
            earlier.AuthorKey.Equals(message.AuthorKey)
                ? earlier.Sequence > message.Sequence
                : earlier.RecordedAt > message.RecordedAt;
    }

    private sealed class KeyOrder : IComparer<byte[]>
    {
        public static KeyOrder Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
