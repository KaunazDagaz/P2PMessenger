using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupConversationViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly GroupId groupId;
    private readonly IUiThread uiThread;
    private readonly TimeZoneInfo timeZone;
    private bool shown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DeleteChatQuestion))]
    private string groupName = string.Empty;

    [ObservableProperty]
    private string membersText = string.Empty;

    [ObservableProperty]
    private string createdByText = string.Empty;

    [ObservableProperty]
    private string membersButtonText = string.Empty;

    [ObservableProperty]
    private bool isMembersShown;

    [ObservableProperty]
    private bool isJoining;

    [ObservableProperty]
    private bool hasNothingToAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaitingText))]
    private string? waitingText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListsDifferText))]
    private string? listsDifferText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEndedOrLeftText))]
    private string? endedOrLeftText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool canWrite;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string draft = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteChatVisible))]
    private bool isConfirmingDeleteChat;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteChatCommand), nameof(CancelDeleteChatCommand))]
    private bool isDeletingChat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteChatQuestion))]
    private bool isInactiveGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeaveVisible))]
    private bool canLeave;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeaveVisible))]
    private bool isConfirmingLeave;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LeaveGroupCommand), nameof(CancelLeaveCommand))]
    private bool isLeaving;

    [ObservableProperty]
    private bool needsSuccessor;

    [ObservableProperty]
    private GroupSuccessorOptionViewModel? selectedSuccessor;

    [ObservableProperty]
    private string leaveQuestion = string.Empty;

    public GroupConversationViewModel(Messenger messenger, GroupId groupId, IUiThread uiThread)
        : this(messenger, groupId, uiThread, TimeZoneInfo.Local)
    {
    }

    public GroupConversationViewModel(Messenger messenger, GroupId groupId, IUiThread uiThread, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeZone);

        this.messenger = messenger;
        this.groupId = groupId;
        this.uiThread = uiThread;
        this.timeZone = timeZone;
        Members.CollectionChanged += OnMembersChanged;
        Messages.CollectionChanged += OnMessagesChanged;
        RefreshGroup();
        RefreshMessages();
    }

    public override string Title => GroupName;

    public GroupId GroupId => groupId;

    public ObservableCollection<GroupMemberItemViewModel> Members { get; } = [];

    public GroupMemberItemViewModel? RemovingMember => Members.FirstOrDefault(static member => member.IsConfirmingRemove);

    public bool HasRemovingMember => RemovingMember is not null;

    public ObservableCollection<GroupSuccessorOptionViewModel> SuccessorCandidates { get; } = [];

    public ObservableCollection<GroupMemberToAddViewModel> MembersToAdd { get; } = [];

    public ObservableCollection<GroupMessageItemViewModel> Messages { get; } = [];

    public GroupMessageItemViewModel? DetailsMessage => Messages.FirstOrDefault(static message => message.IsDetailsShown);

    public bool HasDetailsMessage => DetailsMessage is not null;

    public GroupMessageItemViewModel? DeletingMessage => Messages.FirstOrDefault(static message => message.IsConfirmingDelete);

    public bool HasDeletingMessage => DeletingMessage is not null;

    public bool HasWaitingText => WaitingText is not null;

    public bool HasListsDifferText => ListsDifferText is not null;

    public bool HasEndedOrLeftText => EndedOrLeftText is not null;

    public bool IsDeleteChatVisible => !IsConfirmingDeleteChat;

    public bool IsLeaveVisible => CanLeave && !IsConfirmingLeave;

    public string DeleteChatQuestion => IsInactiveGroup
        ? Formats.With(Formats.GroupDeleteInactiveChatQuestion, GroupName)
        : Formats.With(Formats.GroupDeleteChatQuestion, GroupName);

    private bool IsNotDeletingChat => !IsDeletingChat;

    private bool IsNotLeaving => !IsLeaving;

    private bool CanSend => CanWrite && !IsBusy && !string.IsNullOrWhiteSpace(Draft);

    public override void OnShown()
    {
        shown = true;
        messenger.Groups.Changed += OnGroupChanged;
        messenger.GroupChats.Changed += OnGroupChatChanged;
        messenger.Contacts.Changed += OnContactChanged;
        RefreshGroup();
        RefreshMessages();
        messenger.GroupChats.MarkRead(groupId);
    }

    public override void OnHidden()
    {
        shown = false;
        messenger.Groups.Changed -= OnGroupChanged;
        messenger.GroupChats.Changed -= OnGroupChatChanged;
        messenger.Contacts.Changed -= OnContactChanged;
    }

    [RelayCommand]
    private void ToggleMembers() => IsMembersShown = !IsMembersShown;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            GroupSendResult result = await messenger.SendToGroupAsync(groupId, Draft.TrimEnd(), cancellationToken);
            ErrorMessage = result.Outcome switch
            {
                GroupSendOutcome.Queued => null,
                GroupSendOutcome.Empty => null,
                GroupSendOutcome.TooLong => UiText.GroupTooLong,
                GroupSendOutcome.NotSendable => UiText.ConversationNotSendable,
                GroupSendOutcome.NotCreatedYet => UiText.GroupNotCreatedYet,
                GroupSendOutcome.Left => UiText.GroupSendLeft,
                GroupSendOutcome.Removed => UiText.GroupSendRemoved,
                GroupSendOutcome.Ended => UiText.GroupSendEnded,
                _ => UiText.GroupNotAGroup,
            };

            if (result.IsQueued)
            {
                Draft = string.Empty;
                RefreshMessages();
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.ConversationNotSendable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AskLeave()
    {
        if (!CanLeave)
        {
            return;
        }

        ErrorMessage = null;
        IsConfirmingLeave = true;
    }

    [RelayCommand(CanExecute = nameof(IsNotLeaving))]
    private void CancelLeave() => IsConfirmingLeave = false;

    [RelayCommand(CanExecute = nameof(IsNotLeaving))]
    private async Task LeaveGroupAsync(CancellationToken cancellationToken)
    {
        if (NeedsSuccessor && SelectedSuccessor is null)
        {
            ErrorMessage = UiText.GroupLeaveSuccessorRequired;
            return;
        }

        IsLeaving = true;
        ErrorMessage = null;
        try
        {
            LeaveGroupOutcome outcome = await messenger.LeaveGroupAsync(
                groupId,
                NeedsSuccessor ? SelectedSuccessor!.IdentityKey : null,
                cancellationToken);
            if (outcome == LeaveGroupOutcome.Left)
            {
                IsConfirmingLeave = false;
                IsMembersShown = false;
                RefreshGroup();
            }
            else if (outcome == LeaveGroupOutcome.SuccessorRequired)
            {
                ErrorMessage = UiText.GroupLeaveSuccessorRequired;
            }
            else
            {
                ErrorMessage = UiText.GroupLeaveFailed;
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.GroupLeaveFailed;
        }
        finally
        {
            IsLeaving = false;
        }
    }

    [RelayCommand]
    private void AskDeleteChat()
    {
        ErrorMessage = null;
        IsConfirmingDeleteChat = true;
    }

    [RelayCommand(CanExecute = nameof(IsNotDeletingChat))]
    private void CancelDeleteChat() => IsConfirmingDeleteChat = false;

    [RelayCommand(CanExecute = nameof(IsNotDeletingChat))]
    private async Task DeleteChatAsync(CancellationToken cancellationToken)
    {
        IsDeletingChat = true;
        ErrorMessage = null;
        try
        {
            await messenger.DeleteGroupChatAsync(groupId, cancellationToken);
            Toasts.Show(ToastKind.Success, IsInactiveGroup ? UiText.GroupInactiveChatDeleted : UiText.GroupChatDeleted);
            IsConfirmingDeleteChat = false;
            RefreshMessages();
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.ConversationDeleteChatFailed;
        }
        finally
        {
            IsDeletingChat = false;
        }
    }

    private async Task RemoveMemberAsync(GroupMemberItemViewModel item, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            RemoveMemberResult result = await messenger.RemoveGroupMemberAsync(groupId, item.IdentityKey, cancellationToken);
            if (result.Outcome == RemoveMemberOutcome.Removed)
            {
                Toasts.Show(ToastKind.Success, Formats.With(Formats.GroupMemberRemoved, item.Name));
                RefreshGroup();
            }
            else if (result.Outcome == RemoveMemberOutcome.EndedGroup)
            {
                Toasts.Show(ToastKind.Success, Formats.With(Formats.GroupMemberRemovedEnded, item.Name));
                IsMembersShown = false;
                RefreshGroup();
            }
            else
            {
                ErrorMessage = UiText.GroupRemoveMemberFailed;
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.GroupRemoveMemberFailed;
        }
    }

    private async Task DeleteMessageAsync(GroupMessageItemViewModel item, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            if (await messenger.DeleteWaitingGroupMessageAsync(groupId, item.Id, cancellationToken))
            {
                Toasts.Show(ToastKind.Success, UiText.GroupMessageDeleted);
            }
            else
            {
                ErrorMessage = UiText.GroupMessageNotDeletable;
            }

            RefreshMessages();
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.ConversationMessageDeleteFailed;
        }
    }

    private void OnGroupChanged(object? sender, GroupChangedEventArgs e)
    {
        if (e.Group.Id.Equals(groupId))
        {
            uiThread.Post(RefreshGroup);
        }
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e) => uiThread.Post(() =>
    {
        RefreshGroup();
        RefreshMessages();
    });

    private void OnGroupChatChanged(object? sender, GroupChatChangedEventArgs e)
    {
        if (!e.GroupId.Equals(groupId) || (e.Added is null && e.Updated is null && e.Removed is null && !e.IsCleared))
        {
            return;
        }

        bool added = e.Added is not null;
        uiThread.Post(() =>
        {
            RefreshMessages();
            if (added && shown)
            {
                messenger.GroupChats.MarkRead(groupId);
            }
        });
    }

    private string NameOf(Group group, Ed25519PublicKey memberKey)
    {
        if (memberKey.Equals(messenger.Identity.Keys.IdentityKey))
        {
            return messenger.Identity.Name.Value;
        }

        return messenger.Contacts.TryGet(memberKey, out Contact? contact)
            ? contact.Name.Value
            : group.FindAny(memberKey)?.Name.Value ?? string.Empty;
    }

    private void RefreshGroup()
    {
        if (!messenger.Groups.TryGet(groupId, out Group? group))
        {
            return;
        }

        GroupName = group.Name.Value;
        string separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ";
        MembersText = Formats.With(
            Formats.GroupMembers,
            string.Join(separator, group.Members.Select(member => member.IdentityKey.Equals(group.OwnKey)
                ? Formats.With(Formats.GroupYou, NameOf(group, member.IdentityKey))
                : NameOf(group, member.IdentityKey))));
        CreatedByText = group.YouCreated ? UiText.GroupCreatedBy : Formats.With(Formats.GroupCreatedBy, NameOf(group, group.Creator));
        MembersButtonText = Formats.With(Formats.GroupMembersButton, group.Members.Count);

        HashSet<Ed25519PublicKey> confirmingRemove = [.. Members.Where(static item => item.IsConfirmingRemove).Select(static item => item.IdentityKey)];
        bool lastOther = group.Others.Count() == 1;
        Members.Clear();
        foreach (GroupMemberStatus member in group.Members
            .OrderBy(member => !member.IdentityKey.Equals(group.OwnKey))
            .ThenBy(member => NameOf(group, member.IdentityKey), StringComparer.CurrentCultureIgnoreCase))
        {
            bool isYou = member.IdentityKey.Equals(group.OwnKey);
            bool isAdmin = member.IdentityKey.Equals(group.AuthorityKey);
            bool canRemove = group.CanRemoveMembers && !isYou;
            string memberName = NameOf(group, member.IdentityKey);
            string question = lastOther
                ? Formats.WithAll(Formats.GroupRemoveLastMemberQuestion, memberName, group.Name.Value)
                : Formats.WithAll(Formats.GroupRemoveMemberQuestion, memberName, group.Name.Value);
            GroupMemberItemViewModel item = new(member.IdentityKey, memberName, isYou, isAdmin, canRemove, question, RemoveMemberAsync)
            {
                IsConfirmingRemove = canRemove && confirmingRemove.Contains(member.IdentityKey),
            };
            Members.Add(item);
        }

        Ed25519PublicKey? previousSuccessor = SelectedSuccessor?.IdentityKey;
        SuccessorCandidates.Clear();
        if (group.NeedsSuccessorToLeave)
        {
            foreach (GroupMemberStatus member in group.Others.OrderBy(member => NameOf(group, member.IdentityKey), StringComparer.CurrentCultureIgnoreCase))
            {
                SuccessorCandidates.Add(new GroupSuccessorOptionViewModel(member.IdentityKey, NameOf(group, member.IdentityKey)));
            }
        }

        SelectedSuccessor = previousSuccessor is null
            ? SuccessorCandidates.FirstOrDefault()
            : SuccessorCandidates.FirstOrDefault(option => option.IdentityKey.Equals(previousSuccessor)) ?? SuccessorCandidates.FirstOrDefault();

        NeedsSuccessor = group.NeedsSuccessorToLeave;
        CanLeave = group.CanLeave;
        if (!CanLeave)
        {
            IsConfirmingLeave = false;
        }

        LeaveQuestion = group.NeedsSuccessorToLeave
            ? Formats.With(Formats.GroupLeaveAdminQuestion, group.Name.Value)
            : group.IsAuthority && group.Others.Count() == 1
                ? Formats.With(Formats.GroupLeaveLastMemberQuestion, group.Name.Value)
                : Formats.With(Formats.GroupLeaveQuestion, group.Name.Value);

        IsInactiveGroup = group.State is GroupState.Left or GroupState.Removed or GroupState.Ended;
        EndedOrLeftText = group.State switch
        {
            GroupState.Left => UiText.GroupLeftBanner,
            GroupState.Removed => UiText.GroupRemovedBanner,
            GroupState.Ended => UiText.GroupEndedBanner,
            _ => null,
        };

        IsJoining = group.State == GroupState.Joining;
        MembersToAdd.Clear();
        if (IsJoining)
        {
            foreach (GroupMemberStatus member in messenger.Groups.MembersToAdd(group))
            {
                MembersToAdd.Add(new GroupMemberToAddViewModel(member));
            }
        }

        HasNothingToAdd = IsJoining && MembersToAdd.Count == 0;
        string[] notJoined = [.. group.Others.Where(static member => member.Joined == MemberJoin.NotYet).Select(member => NameOf(group, member.IdentityKey))];
        WaitingText = group.State == GroupState.WaitingForMembers ? Formats.With(Formats.GroupWaiting, string.Join(separator, notJoined)) : null;
        string[] differing = [.. group.Members.Where(static member => member.Joined == MemberJoin.JoinedAnotherList).Select(member => NameOf(group, member.IdentityKey))];
        ListsDifferText = group.State == GroupState.ListsDiffer ? Formats.With(Formats.GroupListsDiffer, string.Join(separator, differing)) : null;
        CanWrite = group.State == GroupState.Created;
    }

    private void RefreshMessages()
    {
        messenger.Groups.TryGet(groupId, out Group? group);
        IReadOnlyList<GroupChatMessage> all = messenger.GroupChats.MessagesIn(groupId);
        HashSet<MessageId> kept = [.. all.Select(static message => message.Id)];
        for (int index = Messages.Count - 1; index >= 0; index--)
        {
            if (!kept.Contains(Messages[index].Id))
            {
                Messages.RemoveAt(index);
            }
        }

        Func<Ed25519PublicKey, string> resolveName = group is null ? static _ => string.Empty : key => NameOf(group, key);
        for (int index = 0; index < all.Count; index++)
        {
            GroupChatMessage message = all[index];
            if (index < Messages.Count && Messages[index].Id.Equals(message.Id))
            {
                Messages[index].Update(message, resolveName);
                continue;
            }

            string sender = resolveName(message.AuthorKey);
            Messages.Insert(index, new GroupMessageItemViewModel(message, sender, timeZone, DeleteMessageAsync, resolveName));
        }

        IsEmpty = Messages.Count == 0;
    }

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GroupMemberItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnMemberChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GroupMemberItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnMemberChanged;
            }
        }

        NotifyRemoving();
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupMemberItemViewModel.IsConfirmingRemove))
        {
            NotifyRemoving();
        }
    }

    private void NotifyRemoving()
    {
        OnPropertyChanged(nameof(RemovingMember));
        OnPropertyChanged(nameof(HasRemovingMember));
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GroupMessageItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnMessageChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GroupMessageItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnMessageChanged;
            }
        }

        NotifyMessageDialogs();
    }

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroupMessageItemViewModel.IsDetailsShown) or nameof(GroupMessageItemViewModel.IsConfirmingDelete))
        {
            NotifyMessageDialogs();
        }
    }

    private void NotifyMessageDialogs()
    {
        OnPropertyChanged(nameof(DetailsMessage));
        OnPropertyChanged(nameof(HasDetailsMessage));
        OnPropertyChanged(nameof(DeletingMessage));
        OnPropertyChanged(nameof(HasDeletingMessage));
    }
}
