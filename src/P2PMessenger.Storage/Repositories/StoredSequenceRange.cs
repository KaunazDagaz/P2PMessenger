using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredSequenceRange(Ed25519PublicKey AuthorKey, long First, long Last);
