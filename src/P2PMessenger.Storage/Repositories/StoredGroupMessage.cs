using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredGroupMessage(
    GroupId GroupId,
    Ed25519PublicKey AuthorKey,
    MessageId Id,
    long Sequence,
    DateTimeOffset WrittenAt,
    string Text,
    DateTimeOffset RecordedAt,
    GroupMessage Envelope)
{
    public IReadOnlyDictionary<Ed25519PublicKey, StoredDeliveryState> Deliveries { get; init; } = new Dictionary<Ed25519PublicKey, StoredDeliveryState>();
}
