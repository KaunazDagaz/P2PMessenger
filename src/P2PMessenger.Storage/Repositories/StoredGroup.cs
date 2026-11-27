using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredGroup(
    GroupRecord Record,
    GroupKey Key,
    IReadOnlyList<StoredGroupMember> Members,
    Ed25519PublicKey AuthorityKey,
    IReadOnlyList<GroupRecord> History,
    IReadOnlyList<GroupLeave> LeaveProofs)
{
    public StoredGroup(GroupRecord record, GroupKey key, IReadOnlyList<StoredGroupMember> members)
        : this(record, key, members, record.Signer, [record], [])
    {
    }
}
