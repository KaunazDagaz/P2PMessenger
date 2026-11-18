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

    internal ChatLog(IChatStore store, ISeenMessageIds seen, TimeProvider timeProvider, IEnumerable<ChatMessage> loaded)
    {
        this.store = store;
        this.seen = seen;
        this.timeProvider = timeProvider;

        foreach (ChatMessage message in loaded.OrderBy(static message => message.RecordedAt))
        {
            ConversationWith(message.ContactKey).Insert(message);
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
            await seen.AddAsync(text.Id, cancellationToken).ConfigureAwait(false);
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

    public void Dispose() => writing.Dispose();

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
            await seen.AddAsync(text.Id, cancellationToken).ConfigureAwait(false);
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

        public void Insert(ChatMessage message)
        {
            int index = Messages.Count;
            while (index > 0 && ComesAfter(Messages[index - 1], message))
            {
                index--;
            }

            Messages.Insert(index, message);
            if (message.Direction == MessageDirection.Outgoing && message.Sequence > LastSentSequence)
            {
                LastSentSequence = message.Sequence;
            }
        }

        private static bool ComesAfter(ChatMessage earlier, ChatMessage message) =>
            earlier.Direction == message.Direction
                ? earlier.Sequence > message.Sequence
                : earlier.RecordedAt > message.RecordedAt;
    }
}
