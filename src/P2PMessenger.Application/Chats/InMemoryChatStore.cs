using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class InMemoryChatStore : IChatStore
{
    private readonly InMemorySeenMessageIds seen;
    private readonly Lock gate = new();
    private readonly List<ChatMessage> messages = [];
    private readonly Dictionary<Ed25519PublicKey, long> sentSequences = [];

    public InMemoryChatStore(InMemorySeenMessageIds seen)
    {
        ArgumentNullException.ThrowIfNull(seen);

        this.seen = seen;
    }

    public ValueTask<IReadOnlyList<ChatMessage>> LoadAllAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ChatMessage>>([.. messages]);
        }
    }

    public ValueTask<IReadOnlyDictionary<Ed25519PublicKey, long>> LoadSentSequencesAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<Ed25519PublicKey, long>>(new Dictionary<Ed25519PublicKey, long>(sentSequences));
        }
    }

    public ValueTask AddAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (gate)
        {
            messages.Add(message);
            seen.Add(message.Id);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            int index = messages.FindIndex(message => message.IsWaiting && message.Id.Equals(id) && message.ContactKey.Equals(contactKey));
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            messages[index] = messages[index].WithDelivery(DeliveryState.Received);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> DeleteChatAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            KeepSentSequence(contactKey);
            return ValueTask.FromResult(messages.RemoveAll(message => message.ContactKey.Equals(contactKey)));
        }
    }

    public ValueTask<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            int index = messages.FindIndex(message =>
                message.IsWaiting
                && message.Direction == MessageDirection.Outgoing
                && message.Id.Equals(id)
                && message.ContactKey.Equals(contactKey));
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            KeepSentSequence(contactKey);
            messages.RemoveAt(index);
            return ValueTask.FromResult(true);
        }
    }

    private void KeepSentSequence(Ed25519PublicKey contactKey)
    {
        long highest = messages
            .Where(message => message.Direction == MessageDirection.Outgoing && message.ContactKey.Equals(contactKey))
            .Select(static message => message.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        if (highest > 0 && (!sentSequences.TryGetValue(contactKey, out long kept) || kept < highest))
        {
            sentSequences[contactKey] = highest;
        }
    }
}
