using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Groups;

public sealed class Group
{
    internal Group(GroupRecord record, IReadOnlyList<GroupMemberStatus> members, Ed25519PublicKey ownKey)
    {
        Record = record;
        Members = members;
        OwnKey = ownKey;
    }

    public GroupRecord Record { get; }

    public GroupId Id => Record.GroupId;

    public DisplayName Name => Record.Name;

    public IReadOnlyList<GroupMemberStatus> Members { get; }

    public Ed25519PublicKey OwnKey { get; }

    public IEnumerable<GroupMemberStatus> Others => Members.Where(member => !member.IdentityKey.Equals(OwnKey));

    public bool YouCreated => Record.Signer.Equals(OwnKey);

    public bool YouJoined => Find(OwnKey)?.Joined == MemberJoin.Joined;

    public bool IsCreated => Members.All(static member => member.Joined == MemberJoin.Joined);

    public bool ListsDiffer => Members.Any(static member => member.Joined == MemberJoin.JoinedAnotherList);

    public GroupState State =>
        ListsDiffer ? GroupState.ListsDiffer
        : !YouJoined ? GroupState.Joining
        : IsCreated ? GroupState.Created
        : GroupState.WaitingForMembers;

    public GroupMemberStatus? Find(Ed25519PublicKey identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        foreach (GroupMemberStatus member in Members)
        {
            if (member.IdentityKey.Equals(identityKey))
            {
                return member;
            }
        }

        return null;
    }

    internal Group With(IReadOnlyCollection<GroupMemberStatus> changed) =>
        new(Record, [.. Members.Select(member => changed.FirstOrDefault(update => update.IdentityKey.Equals(member.IdentityKey)) ?? member)], OwnKey);
}
