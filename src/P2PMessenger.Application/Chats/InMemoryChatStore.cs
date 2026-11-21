using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class InMemoryChatStore : IChatStore
{
    private readonly InMemorySeenMessageIds seen;
    private readonly Lock gate = new();
    private readonly List<ChatMessage> messages = [];

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
}
