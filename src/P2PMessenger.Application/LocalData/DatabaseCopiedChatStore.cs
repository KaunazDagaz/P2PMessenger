using P2PMessenger.Application.Copy;
using P2PMessenger.Core.Copy;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

internal sealed class DatabaseCopiedChatStore : ICopiedChatStore
{
    private readonly CopiedConversationRepository repository;

    public DatabaseCopiedChatStore(CopiedConversationRepository repository)
    {
        this.repository = repository;
    }

    public async ValueTask<IReadOnlyList<CopiedConversation>> LoadAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredCopiedConversation> stored = await repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. stored.Select(ToConversation)];
    }

    public async ValueTask SaveAsync(CopiedConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await repository.SaveAsync(ToStored(conversation), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteAsync(CopyOriginId origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return await repository.DeleteAsync(origin, cancellationToken).ConfigureAwait(false);
    }

    private static CopiedConversation ToConversation(StoredCopiedConversation stored) =>
        new(
            stored.Origin,
            stored.Title,
            stored.CopiedAt,
            [.. stored.Messages.Select(static message => new CopiedMessage(message.Position, message.WrittenByOwner, message.AuthorName, message.Text, message.WrittenAt))]);

    private static StoredCopiedConversation ToStored(CopiedConversation conversation) =>
        new(
            conversation.Origin,
            conversation.Title,
            conversation.CopiedAt,
            [.. conversation.Messages.Select(static message => new StoredCopiedMessage(message.Position, message.WrittenByOwner, message.AuthorName, message.Text, message.WrittenAt))]);
}
