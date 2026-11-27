using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Groups;

public sealed record GroupMemberStatus
{
    public GroupMemberStatus(Ed25519PublicKey identityKey, DisplayName name, MemberJoin joined, NoticeState invitation, NoticeState ownJoin)
        : this(identityKey, name, GroupMemberState.Member, joined, invitation, ownJoin, NoticeState.None, NoticeState.None, NoticeState.None, 0)
    {
    }

    public GroupMemberStatus(
        Ed25519PublicKey identityKey,
        DisplayName name,
        GroupMemberState state,
        MemberJoin joined,
        NoticeState invitation,
        NoticeState ownJoin,
        NoticeState update,
        NoticeState leave,
        NoticeState removed,
        int removedEpoch)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);

        IdentityKey = identityKey;
        Name = name;
        State = state;
        Joined = joined;
        Invitation = invitation;
        OwnJoin = ownJoin;
        Update = update;
        Leave = leave;
        Removed = removed;
        RemovedEpoch = removedEpoch;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public DisplayName Name { get; }

    public GroupMemberState State { get; init; }

    public MemberJoin Joined { get; init; }

    public NoticeState Invitation { get; init; }

    public NoticeState OwnJoin { get; init; }

    public NoticeState Update { get; init; }

    public NoticeState Leave { get; init; }

    public NoticeState Removed { get; init; }

    public int RemovedEpoch { get; init; }
}
