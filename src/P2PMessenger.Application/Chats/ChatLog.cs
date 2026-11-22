using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class ChatLog : IDisposable
{
    private readonly IChatStore store;
    private readonly ISeenMessageIds seen;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, Conversation> conversations = [];
    private readonly Dictionary<Ed25519PublicKey, SemaphoreSlim> deliveries = [];

    internal ChatLog(IChatStore store, ISeenMessageIds seen, TimeProvider timeProvider, IEnumerable<ChatMessage> loaded)
        : this(store, seen, timeProvider, loaded, new Dictionary<Ed25519PublicKey, long>())
    {
    }

    internal ChatLog(
        IChatStore store,
        ISeenMessageIds seen,
        TimeProvider timeProvider,
        IEnumerable<ChatMessage> loaded,
        IReadOnlyDictionary<Ed25519PublicKey, long> sentSequences)
    {
        this.store = store;
        this.seen = seen;
        this.timeProvider = timeProvider;

        foreach (ChatMessage message in loaded.OrderBy(static message => message.RecordedAt))
        {
            ConversationWith(message.ContactKey).Insert(message);
        }

        foreach ((Ed25519PublicKey contactKey, long sequence) in sentSequences)
        {
            ConversationWith(contactKey).KeepSentSequence(sequence);
        }
    }

    public event EventHandler<ChatChangedEventArgs>? Changed;

    public IReadOnlyList<ChatMessage> MessagesWith(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return conversations.TryGetValue(contactKey, out Conversation? conversation) ? [.. conversation.Messages] : [];
        }
    }

    public IReadOnlyList<ChatMessage> WaitingFor(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return conversations.TryGetValue(contactKey, out Conversation? conversation) ? conversation.Waiting() : [];
        }
    }

    public IReadOnlyList<Ed25519PublicKey> ContactsWithWaitingMessages()
    {
        lock (gate)
        {
            return [.. conversations.Where(static pair => pair.Value.HasWaiting()).Select(static pair => pair.Key)];
        }
    }

    public int UnreadCount(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return conversations.TryGetValue(contactKey, out Conversation? conversation) ? conversation.Unread : 0;
        }
    }

    public void MarkRead(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            if (!conversations.TryGetValue(contactKey, out Conversation? conversation) || conversation.Unread == 0)
            {
                return;
            }

            conversation.Unread = 0;
        }

        Changed?.Invoke(this, new ChatChangedEventArgs(contactKey, null));
    }

    public async Task<bool> ReceiveAsync(Ed25519PublicKey contactKey, TextMessage text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(text);

        ChatMessage message;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await seen.ContainsAsync(text.Id, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            message = ChatMessage.From(contactKey, MessageDirection.Incoming, text, timeProvider.GetUtcNow());
            await store.AddAsync(message, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                Conversation conversation = ConversationWith(contactKey);
                conversation.Insert(message);
                conversation.Unread++;
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new ChatChangedEventArgs(contactKey, message));
        return true;
    }

    public async Task<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        ChatMessage received;
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ChatMessage? waiting;
            lock (gate)
            {
                waiting = conversations.TryGetValue(contactKey, out Conversation? conversation) ? conversation.FindWaiting(id) : null;
            }

            if (waiting is null || !await store.MarkReceivedAsync(contactKey, id, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            received = waiting.WithDelivery(DeliveryState.Received);
            lock (gate)
            {
                ConversationWith(contactKey).Replace(waiting, received);
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new ChatChangedEventArgs(contactKey, null, received));
        return true;
    }

    public async Task<int> DeleteChatAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        int removed;
        using (await EnterDeliveryAsync(contactKey, cancellationToken).ConfigureAwait(false))
        {
            await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                removed = await store.DeleteChatAsync(contactKey, cancellationToken).ConfigureAwait(false);
                lock (gate)
                {
                    if (conversations.TryGetValue(contactKey, out Conversation? conversation))
                    {
                        conversation.Clear();
                    }
                }
            }
            finally
            {
                writing.Release();
            }
        }

        Changed?.Invoke(this, ChatChangedEventArgs.ForCleared(contactKey));
        return removed;
    }

    public async Task<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        ChatMessage deleted;
        using (await EnterDeliveryAsync(contactKey, cancellationToken).ConfigureAwait(false))
        {
            await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ChatMessage? waiting;
                lock (gate)
                {
                    waiting = conversations.TryGetValue(contactKey, out Conversation? conversation) ? conversation.FindWaiting(id) : null;
                }

                if (waiting is null
                    || waiting.Direction != MessageDirection.Outgoing
                    || !await store.DeleteWaitingAsync(contactKey, id, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                lock (gate)
                {
                    ConversationWith(contactKey).Remove(waiting);
                }

                deleted = waiting;
            }
            finally
            {
                writing.Release();
            }
        }

        Changed?.Invoke(this, ChatChangedEventArgs.ForRemoved(deleted));
        return true;
    }

    public void Dispose()
    {
        writing.Dispose();
        lock (gate)
        {
            foreach (SemaphoreSlim delivery in deliveries.Values)
            {
                delivery.Dispose();
            }

            deliveries.Clear();
        }
    }

    internal async Task<IDisposable> EnterDeliveryAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        SemaphoreSlim delivery;
        lock (gate)
        {
            if (!deliveries.TryGetValue(contactKey, out SemaphoreSlim? existing))
            {
                existing = new SemaphoreSlim(1, 1);
                deliveries.Add(contactKey, existing);
            }

            delivery = existing;
        }

        await delivery.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DeliveryTurn(delivery);
    }

    internal bool IsWaiting(Ed25519PublicKey contactKey, MessageId id)
    {
        lock (gate)
        {
            return conversations.TryGetValue(contactKey, out Conversation? conversation) && conversation.FindWaiting(id) is not null;
        }
    }

    internal long NextSequence(Ed25519PublicKey contactKey)
    {
        lock (gate)
        {
            return ++ConversationWith(contactKey).LastSentSequence;
        }
    }

    internal async Task<ChatMessage> RecordOutgoingAsync(
        Ed25519PublicKey contactKey,
        TextMessage text,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        ChatMessage message = ChatMessage.From(contactKey, MessageDirection.Outgoing, text, recordedAt);
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.AddAsync(message, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                ConversationWith(contactKey).Insert(message);
            }
        }
        finally
        {
            writing.Release();
        }

        Changed?.Invoke(this, new ChatChangedEventArgs(contactKey, message));
        return message;
    }

    private Conversation ConversationWith(Ed25519PublicKey contactKey)
    {
        if (!conversations.TryGetValue(contactKey, out Conversation? conversation))
        {
            conversation = new Conversation();
            conversations.Add(contactKey, conversation);
        }

        return conversation;
    }

    private sealed class Conversation
    {
        public List<ChatMessage> Messages { get; } = [];

        public long LastSentSequence { get; set; }

        public int Unread { get; set; }

        public int WaitingCount { get; private set; }

        public void Insert(ChatMessage message)
        {
            int index = Messages.Count;
            while (index > 0 && ComesAfter(Messages[index - 1], message))
            {
                index--;
            }

            Messages.Insert(index, message);
            if (message.IsWaiting)
            {
                WaitingCount++;
            }

            if (message.Direction == MessageDirection.Outgoing && message.Sequence > LastSentSequence)
            {
                LastSentSequence = message.Sequence;
            }
        }

        public ChatMessage? FindWaiting(MessageId id) => Messages.Find(message => message.IsWaiting && message.Id.Equals(id));

        public bool HasWaiting() => WaitingCount > 0;

        public IReadOnlyList<ChatMessage> Waiting() =>
            [.. Messages.Where(static message => message.IsWaiting).OrderBy(static message => message.Sequence)];

        public void KeepSentSequence(long sequence)
        {
            if (sequence > LastSentSequence)
            {
                LastSentSequence = sequence;
            }
        }

        public void Clear()
        {
            Messages.Clear();
            Unread = 0;
            WaitingCount = 0;
        }

        public void Remove(ChatMessage message)
        {
            if (Messages.Remove(message) && message.IsWaiting)
            {
                WaitingCount--;
            }
        }

        public void Replace(ChatMessage old, ChatMessage replacement)
        {
            int index = Messages.IndexOf(old);
            if (index < 0)
            {
                return;
            }

            Messages[index] = replacement;
            WaitingCount += (replacement.IsWaiting ? 1 : 0) - (old.IsWaiting ? 1 : 0);
        }

        private static bool ComesAfter(ChatMessage earlier, ChatMessage message) =>
            earlier.Direction == message.Direction
                ? earlier.Sequence > message.Sequence
                : earlier.RecordedAt > message.RecordedAt;
    }

    private sealed class DeliveryTurn : IDisposable
    {
        private SemaphoreSlim? delivery;

        public DeliveryTurn(SemaphoreSlim delivery)
        {
            this.delivery = delivery;
        }

        public void Dispose() => Interlocked.Exchange(ref delivery, null)?.Release();
    }
}
