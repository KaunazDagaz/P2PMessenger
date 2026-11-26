using P2PMessenger.Core.Copy;

namespace P2PMessenger.Application.Copy;

public interface ICopiedChatStore
{
    ValueTask<IReadOnlyList<CopiedConversation>> LoadAllAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(CopiedConversation conversation, CancellationToken cancellationToken);

    ValueTask<bool> DeleteAsync(CopyOriginId origin, CancellationToken cancellationToken);
}
