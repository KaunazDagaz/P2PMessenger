using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Chats;

public sealed class ChatMessage
{
    public ChatMessage(
        Ed25519PublicKey contactKey,
        MessageDirection direction,
        MessageId id,
        long sequence,
        DateTimeOffset writtenAt,
        string text,
        DateTimeOffset recordedAt)
        : this(contactKey, direction, id, sequence, writtenAt, text, recordedAt, direction == MessageDirection.Outgoing ? DeliveryState.Waiting : DeliveryState.None)
    {
    }

    public ChatMessage(
        Ed25519PublicKey contactKey,
        MessageDirection direction,
        MessageId id,
        long sequence,
        DateTimeOffset writtenAt,
        string text,
        DateTimeOffset recordedAt,
        DeliveryState delivery)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);

        bool fits = direction == MessageDirection.Outgoing
            ? delivery is DeliveryState.Waiting or DeliveryState.Received
            : delivery == DeliveryState.None;
        if (!fits)
        {
            throw new ArgumentException("A sent message is waiting or received by the contact's app, and a received message has no delivery state.", nameof(delivery));
        }

        ContactKey = contactKey;
        Direction = direction;
        Id = id;
        Sequence = sequence;
        WrittenAt = writtenAt;
        Text = text;
        RecordedAt = recordedAt;
        Delivery = delivery;
    }

    public Ed25519PublicKey ContactKey { get; }

    public MessageDirection Direction { get; }

    public MessageId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    public DateTimeOffset RecordedAt { get; }

    public DeliveryState Delivery { get; }

    public bool IsWaiting => Delivery == DeliveryState.Waiting;

    internal static ChatMessage From(Ed25519PublicKey contactKey, MessageDirection direction, TextMessage text, DateTimeOffset recordedAt) =>
        new(contactKey, direction, text.Id, text.Sequence, text.WrittenAt, text.Text, recordedAt);

    internal ChatMessage WithDelivery(DeliveryState delivery) =>
        new(ContactKey, Direction, Id, Sequence, WrittenAt, Text, RecordedAt, delivery);

    internal bool TryRebuild([NotNullWhen(true)] out TextMessage? text) => TextMessage.TryCreate(Id, Sequence, WrittenAt, Text, out text);
}
