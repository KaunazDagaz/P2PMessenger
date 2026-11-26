using P2PMessenger.Core.Groups;

namespace P2PMessenger.Storage.Repositories;

public sealed record StoredGroup(GroupRecord Record, GroupKey Key, IReadOnlyList<StoredGroupMember> Members);
