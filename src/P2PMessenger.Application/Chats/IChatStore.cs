using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public interface IChatStore
{
    ValueTask<IReadOnlyList<ChatMessage>> LoadAllAsync(CancellationToken cancellationToken);

    ValueTask AddAsync(ChatMessage message, CancellationToken cancellationToken);

    ValueTask<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken);
}
