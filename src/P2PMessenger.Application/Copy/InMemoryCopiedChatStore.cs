using P2PMessenger.Core.Copy;

namespace P2PMessenger.Application.Copy;

public sealed class InMemoryCopiedChatStore : ICopiedChatStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<CopyOriginId, CopiedConversation> conversations = [];

    public ValueTask<IReadOnlyList<CopiedConversation>> LoadAllAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<CopiedConversation>>([.. conversations.Values]);
        }
    }

    public ValueTask SaveAsync(CopiedConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        lock (gate)
        {
            conversations[conversation.Origin] = conversation;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(CopyOriginId origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        lock (gate)
        {
            return ValueTask.FromResult(conversations.Remove(origin));
        }
    }
}
