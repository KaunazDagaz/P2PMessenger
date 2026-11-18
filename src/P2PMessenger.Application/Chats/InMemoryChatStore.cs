namespace P2PMessenger.Application.Chats;

public sealed class InMemoryChatStore : IChatStore
{
    private readonly Lock gate = new();
    private readonly List<ChatMessage> messages = [];

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
        }

        return ValueTask.CompletedTask;
    }
}
