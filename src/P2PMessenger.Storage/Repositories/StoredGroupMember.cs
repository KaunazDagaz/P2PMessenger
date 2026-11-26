using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredGroupMember(Ed25519PublicKey IdentityKey, StoredMemberJoin Joined, StoredNoticeState Invitation, StoredNoticeState OwnJoin);
