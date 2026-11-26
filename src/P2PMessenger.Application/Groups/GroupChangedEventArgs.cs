namespace P2PMessenger.Application.Groups;

public sealed class GroupChangedEventArgs : EventArgs
{
    public GroupChangedEventArgs(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Group = group;
    }

    public Group Group { get; }
}
