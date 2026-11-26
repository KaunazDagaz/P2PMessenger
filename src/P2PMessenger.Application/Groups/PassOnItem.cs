using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

internal readonly record struct PassOnItem(GroupId GroupId, MessageId Id, Ed25519PublicKey AuthorKey, long Sequence);
