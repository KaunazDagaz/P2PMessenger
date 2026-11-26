using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Groups;

public sealed record DeletedRange(Ed25519PublicKey AuthorKey, long First, long Last);
