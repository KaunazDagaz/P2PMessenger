using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Application.Groups;

public sealed record LoadedGroup(
    GroupRecord Record,
    GroupKey Key,
    IReadOnlyList<GroupMemberStatus> Members,
    Ed25519PublicKey AuthorityKey,
    IReadOnlyList<GroupRecord> History,
    IReadOnlyList<GroupLeave> LeaveProofs)
{
    public LoadedGroup(GroupRecord record, GroupKey key, IReadOnlyList<GroupMemberStatus> members)
        : this(record, key, members, record.Signer, [record], [])
    {
    }
}
