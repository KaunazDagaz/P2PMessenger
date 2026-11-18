namespace P2PMessenger.Application.Chats;

public interface IChatStore
{
    ValueTask<IReadOnlyList<ChatMessage>> LoadAllAsync(CancellationToken cancellationToken);

    ValueTask AddAsync(ChatMessage message, CancellationToken cancellationToken);
}
