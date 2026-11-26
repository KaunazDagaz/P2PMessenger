using P2PMessenger.Core.Groups;

namespace P2PMessenger.Application.Groups;

public sealed class GroupChatChangedEventArgs : EventArgs
{
    public GroupChatChangedEventArgs(GroupId groupId, GroupChatMessage? added, GroupChatMessage? updated)
        : this(groupId, added, updated, null, false)
    {
    }

    private GroupChatChangedEventArgs(GroupId groupId, GroupChatMessage? added, GroupChatMessage? updated, GroupChatMessage? removed, bool isCleared)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        GroupId = groupId;
        Added = added;
        Updated = updated;
        Removed = removed;
        IsCleared = isCleared;
    }

    public GroupId GroupId { get; }

    public GroupChatMessage? Added { get; }

    public GroupChatMessage? Updated { get; }

    public GroupChatMessage? Removed { get; }

    public bool IsCleared { get; }

    public static GroupChatChangedEventArgs ForRemoved(GroupChatMessage removed)
    {
        ArgumentNullException.ThrowIfNull(removed);

        return new GroupChatChangedEventArgs(removed.GroupId, null, null, removed, false);
    }

    public static GroupChatChangedEventArgs ForCleared(GroupId groupId) => new(groupId, null, null, null, true);
}
