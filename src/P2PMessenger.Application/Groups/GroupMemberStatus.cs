using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Groups;

public sealed record GroupMemberStatus
{
    public GroupMemberStatus(Ed25519PublicKey identityKey, DisplayName name, MemberJoin joined, NoticeState invitation, NoticeState ownJoin)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);

        IdentityKey = identityKey;
        Name = name;
        Joined = joined;
        Invitation = invitation;
        OwnJoin = ownJoin;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public DisplayName Name { get; }

    public MemberJoin Joined { get; init; }

    public NoticeState Invitation { get; init; }

    public NoticeState OwnJoin { get; init; }
}
