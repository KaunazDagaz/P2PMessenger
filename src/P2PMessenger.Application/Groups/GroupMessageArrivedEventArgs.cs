using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Groups;

internal sealed record GroupMessageArrivedEventArgs(Ed25519PublicKey SenderKey, GroupChatMessage Message);
