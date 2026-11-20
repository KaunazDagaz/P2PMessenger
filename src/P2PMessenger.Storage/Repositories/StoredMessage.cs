using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredMessage(
    Ed25519PublicKey ContactKey,
    StoredMessageDirection Direction,
    MessageId Id,
    long Sequence,
    DateTimeOffset WrittenAt,
    string Text,
    DateTimeOffset RecordedAt);
