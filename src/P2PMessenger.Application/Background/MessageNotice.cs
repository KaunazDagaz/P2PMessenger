using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Background;

public sealed record MessageNotice(Ed25519PublicKey ContactKey, string SenderName, string? Preview);
