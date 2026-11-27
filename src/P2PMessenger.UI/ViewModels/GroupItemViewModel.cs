using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Groups;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed class GroupItemViewModel : ViewModelBase
{
    public GroupItemViewModel(Group group, Messenger messenger, Action<Group> open)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(open);

        Group = group;
        Name = group.Name.Value;
        int joined = group.Members.Count(static member => member.Joined == MemberJoin.Joined);
        StateText = group.State switch
        {
            GroupState.Created => Formats.With(Formats.GroupItemMembers, group.Members.Count),
            GroupState.Joining => UiText.GroupItemJoining,
            GroupState.WaitingForMembers => Formats.WithAll(Formats.GroupItemWaiting, group.Members.Count - joined, group.Members.Count),
            GroupState.Left => UiText.GroupItemLeft,
            GroupState.Removed => UiText.GroupItemRemoved,
            GroupState.Ended => UiText.GroupItemEnded,
            _ => UiText.GroupItemListsDiffer,
        };
        int unread = messenger.GroupChats.UnreadCount(group.Id);
        UnreadText = unread > 0 ? Formats.With(Formats.Unread, unread) : null;
        OpenCommand = new RelayCommand(() => open(group));
    }

    public Group Group { get; }

    public string Name { get; }

    public string StateText { get; }

    public string? UnreadText { get; }

    public bool HasUnread => UnreadText is not null;

    public IRelayCommand OpenCommand { get; }
}
