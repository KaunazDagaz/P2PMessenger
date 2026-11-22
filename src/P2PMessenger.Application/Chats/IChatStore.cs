using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public interface IChatStore
{
    ValueTask<IReadOnlyList<ChatMessage>> LoadAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<Ed25519PublicKey, long>> LoadSentSequencesAsync(CancellationToken cancellationToken);

    ValueTask AddAsync(ChatMessage message, CancellationToken cancellationToken);

    ValueTask<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken);

    ValueTask<int> DeleteChatAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken);

    ValueTask<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken);
}
