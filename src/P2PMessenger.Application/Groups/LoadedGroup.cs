using P2PMessenger.Core.Groups;

namespace P2PMessenger.Application.Groups;

public sealed record LoadedGroup(GroupRecord Record, GroupKey Key, IReadOnlyList<GroupMemberStatus> Members);
