using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
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

    public async ValueTask<bool> MarkReceivedAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken) =>
        await repository.MarkReceivedAsync(contactKey, id, cancellationToken).ConfigureAwait(false);

    private static DeliveryState ToDelivery(StoredDeliveryState stored) => stored switch
    {
        StoredDeliveryState.Waiting => DeliveryState.Waiting,
        StoredDeliveryState.Received => DeliveryState.Received,
        _ => DeliveryState.None,
    };

    private static StoredDeliveryState ToStored(DeliveryState delivery) => delivery switch
    {
        DeliveryState.Waiting => StoredDeliveryState.Waiting,
        DeliveryState.Received => StoredDeliveryState.Received,
        _ => StoredDeliveryState.None,
    };

    private static ChatMessage ToChatMessage(StoredMessage stored) =>
        new(
            stored.ContactKey,
            stored.Direction == StoredMessageDirection.Outgoing ? MessageDirection.Outgoing : MessageDirection.Incoming,
            stored.Id,
            stored.Sequence,
            stored.WrittenAt,
            stored.Text,
            stored.RecordedAt,
            ToDelivery(stored.Delivery));

    private static StoredMessage ToStored(ChatMessage message) =>
        new(
            message.ContactKey,
            message.Direction == MessageDirection.Outgoing ? StoredMessageDirection.Outgoing : StoredMessageDirection.Incoming,
            message.Id,
            message.Sequence,
            message.WrittenAt,
            message.Text,
            message.RecordedAt)
        {
            Delivery = ToStored(message.Delivery),
        };
}
