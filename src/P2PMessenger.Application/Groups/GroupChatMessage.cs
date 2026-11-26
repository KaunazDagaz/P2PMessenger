using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

public sealed class GroupChatMessage
{
    public GroupChatMessage(
        GroupMessage envelope,
        MessageId id,
        long sequence,
        DateTimeOffset writtenAt,
        string text,
        DateTimeOffset recordedAt,
        IReadOnlyDictionary<Ed25519PublicKey, DeliveryState> deliveries)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Values.Any(static state => state is not (DeliveryState.Waiting or DeliveryState.Received)))
        {
            throw new ArgumentException("A member's delivery is waiting or received by that member's app.", nameof(deliveries));
        }

        Envelope = envelope;
        Id = id;
        Sequence = sequence;
        WrittenAt = writtenAt;
        Text = text;
        RecordedAt = recordedAt;
        Deliveries = deliveries;
    }

    public GroupMessage Envelope { get; }

    public GroupId GroupId => Envelope.GroupId;

    public Ed25519PublicKey AuthorKey => Envelope.AuthorKey;

    public MessageDirection Direction => Deliveries.Count > 0 ? MessageDirection.Outgoing : MessageDirection.Incoming;

    public MessageId Id { get; }

    public long Sequence { get; }

    public DateTimeOffset WrittenAt { get; }

    public string Text { get; }

    public DateTimeOffset RecordedAt { get; }

    public IReadOnlyDictionary<Ed25519PublicKey, DeliveryState> Deliveries { get; }

    public int RecipientCount => Deliveries.Count;

    public int ReceivedCount => Deliveries.Values.Count(static state => state == DeliveryState.Received);

    public bool CanBeDeleted => RecipientCount > 0 && ReceivedCount == 0;

    public bool IsWaitingFor(Ed25519PublicKey memberKey) =>
        Deliveries.TryGetValue(memberKey, out DeliveryState state) && state == DeliveryState.Waiting;

    internal GroupChatMessage WithReceivedBy(Ed25519PublicKey memberKey)
    {
        Dictionary<Ed25519PublicKey, DeliveryState> deliveries = new(Deliveries) { [memberKey] = DeliveryState.Received };
        return new GroupChatMessage(Envelope, Id, Sequence, WrittenAt, Text, RecordedAt, deliveries);
    }
}
