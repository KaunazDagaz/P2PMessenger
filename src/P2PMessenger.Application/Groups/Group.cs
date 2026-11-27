using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Groups;

public sealed class Group
{
    internal Group(
        GroupRecord record,
        IReadOnlyList<GroupMemberStatus> allMembers,
        Ed25519PublicKey ownKey,
        Ed25519PublicKey? authorityKey = null,
        IReadOnlyList<GroupRecord>? history = null,
        IReadOnlyList<GroupLeave>? leaveProofs = null)
    {
        Record = record;
        AllMembers = allMembers;
        Members = [.. allMembers.Where(static member => member.State == GroupMemberState.Member)];
        OwnKey = ownKey;
        AuthorityKey = authorityKey ?? record.Signer;
        History = history ?? [record];
        LeaveProofs = leaveProofs ?? [];
    }

    public GroupRecord Record { get; }

    public IReadOnlyList<GroupRecord> History { get; }

    public GroupId Id => Record.GroupId;

    public DisplayName Name => Record.Name;

    public IReadOnlyList<GroupMemberStatus> AllMembers { get; }

    public IReadOnlyList<GroupMemberStatus> Members { get; }

    public Ed25519PublicKey OwnKey { get; }

    public Ed25519PublicKey AuthorityKey { get; }

    public IReadOnlyList<GroupLeave> LeaveProofs { get; }

    public IEnumerable<GroupMemberStatus> Others => Members.Where(member => !member.IdentityKey.Equals(OwnKey));

    public Ed25519PublicKey Creator => History[0].Signer;

    public bool YouCreated => Creator.Equals(OwnKey);

    public bool IsAuthority => AuthorityKey.Equals(OwnKey);

    public GroupMemberStatus OwnMember => FindAny(OwnKey)!;

    public bool IsLeft => OwnMember.State == GroupMemberState.Left;

    public bool IsRemoved => OwnMember.State == GroupMemberState.Removed;

    public bool IsEnded => !IsLeft && !IsRemoved && Members.Count < GroupRecord.MinMembers;

    public bool YouJoined => Find(OwnKey)?.Joined == MemberJoin.Joined;

    public bool IsCreated => Members.Count >= GroupRecord.MinMembers && Members.All(static member => member.Joined == MemberJoin.Joined);

    public bool ListsDiffer => Members.Any(static member => member.Joined == MemberJoin.JoinedAnotherList);

    public bool CanRemoveMembers =>
        IsAuthority
        && State is GroupState.Created or GroupState.WaitingForMembers or GroupState.Joining
        && Others.Any();

    public bool CanLeave => State is not (GroupState.Left or GroupState.Removed or GroupState.Ended);

    public bool NeedsSuccessorToLeave => IsAuthority && CanLeave && Others.Count() >= 2;

    public GroupState State =>
        IsRemoved ? GroupState.Removed
        : IsLeft ? GroupState.Left
        : IsEnded ? GroupState.Ended
        : ListsDiffer ? GroupState.ListsDiffer
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

    public GroupMemberStatus? FindAny(Ed25519PublicKey identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        foreach (GroupMemberStatus member in AllMembers)
        {
            if (member.IdentityKey.Equals(identityKey))
            {
                return member;
            }
        }

        return null;
    }

    internal Group With(IReadOnlyCollection<GroupMemberStatus> changed) =>
        new(
            Record,
            [.. AllMembers.Select(member => changed.FirstOrDefault(update => update.IdentityKey.Equals(member.IdentityKey)) ?? member)],
            OwnKey,
            AuthorityKey,
            History,
            LeaveProofs);

    internal Group WithMembership(
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> changed,
        IReadOnlyList<GroupLeave> leaveProofs) =>
        new(
            Record,
            [.. AllMembers.Select(member => changed.FirstOrDefault(update => update.IdentityKey.Equals(member.IdentityKey)) ?? member)],
            OwnKey,
            authorityKey,
            History,
            leaveProofs);

    internal Group WithEpoch(
        GroupRecord record,
        Ed25519PublicKey authorityKey,
        IReadOnlyCollection<GroupMemberStatus> changed,
        IReadOnlyList<GroupLeave> leaveProofs) =>
        new(
            record,
            [.. AllMembers.Select(member => changed.FirstOrDefault(update => update.IdentityKey.Equals(member.IdentityKey)) ?? member)],
            OwnKey,
            authorityKey,
            [.. History, record],
            leaveProofs);
}
