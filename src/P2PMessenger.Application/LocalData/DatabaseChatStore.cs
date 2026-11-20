using P2PMessenger.Application.Chats;
using P2PMessenger.Storage.Repositories;

namespace P2PMessenger.Application.LocalData;

internal sealed class DatabaseChatStore : IChatStore
{
    private readonly MessageRepository repository;

    public DatabaseChatStore(MessageRepository repository)
    {
        this.repository = repository;
    }

    public async ValueTask<IReadOnlyList<ChatMessage>> LoadAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredMessage> stored = await repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. stored.Select(ToChatMessage)];
    }

    public async ValueTask AddAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await repository.AddAsync(ToStored(message), cancellationToken).ConfigureAwait(false);
    }

    private static ChatMessage ToChatMessage(StoredMessage stored) =>
        new(
            stored.ContactKey,
            stored.Direction == StoredMessageDirection.Outgoing ? MessageDirection.Outgoing : MessageDirection.Incoming,
            stored.Id,
            stored.Sequence,
            stored.WrittenAt,
            stored.Text,
            stored.RecordedAt);

    private static StoredMessage ToStored(ChatMessage message) =>
        new(
            message.ContactKey,
            message.Direction == MessageDirection.Outgoing ? StoredMessageDirection.Outgoing : StoredMessageDirection.Incoming,
            message.Id,
            message.Sequence,
            message.WrittenAt,
            message.Text,
            message.RecordedAt);
}
