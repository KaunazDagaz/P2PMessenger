using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredGroupMember(
    Ed25519PublicKey IdentityKey,
    StoredGroupMemberState State,
    StoredMemberJoin Joined,
    StoredNoticeState Invitation,
    StoredNoticeState OwnJoin,
    StoredNoticeState Update,
    StoredNoticeState Leave,
    StoredNoticeState Removed,
    int RemovedEpoch)
{
    public StoredGroupMember(Ed25519PublicKey identityKey, StoredMemberJoin joined, StoredNoticeState invitation, StoredNoticeState ownJoin)
        : this(identityKey, StoredGroupMemberState.Member, joined, invitation, ownJoin, StoredNoticeState.None, StoredNoticeState.None, StoredNoticeState.None, 0)
    {
    }
}
