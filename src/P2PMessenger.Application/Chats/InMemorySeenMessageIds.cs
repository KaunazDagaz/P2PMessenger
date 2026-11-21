using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class InMemorySeenMessageIds : ISeenMessageIds
{
    private readonly Lock gate = new();
    private readonly HashSet<MessageId> seen = [];

    public ValueTask<bool> ContainsAsync(MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            return ValueTask.FromResult(seen.Contains(id));
        }
    }

    public ValueTask AddAsync(MessageId id, CancellationToken cancellationToken)
    {
        Add(id);
        return ValueTask.CompletedTask;
    }

    internal void Add(MessageId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            seen.Add(id);
        }
    }
}
