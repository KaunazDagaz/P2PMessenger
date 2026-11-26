using System.Collections.ObjectModel;
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
    [NotifyPropertyChangedFor(nameof(HasDeletedText))]
    private string? deletedText;

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
        RefreshGroup();
        RefreshMessages();
    }

    public override string Title => GroupName;

    public GroupId GroupId => groupId;

    public ObservableCollection<GroupMemberToAddViewModel> MembersToAdd { get; } = [];

    public ObservableCollection<GroupMessageItemViewModel> Messages { get; } = [];

    public bool HasWaitingText => WaitingText is not null;

    public bool HasListsDifferText => ListsDifferText is not null;

    public bool HasDeletedText => DeletedText is not null;

    public bool IsDeleteChatVisible => !IsConfirmingDeleteChat;

    public string DeleteChatQuestion => Formats.With(Formats.GroupDeleteChatQuestion, GroupName);

    private bool IsNotDeletingChat => !IsDeletingChat;

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
            DeletedText = UiText.GroupChatDeleted;
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

    private async Task DeleteMessageAsync(GroupMessageItemViewModel item, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            if (await messenger.DeleteWaitingGroupMessageAsync(groupId, item.Id, cancellationToken))
            {
                DeletedText = UiText.GroupMessageDeleted;
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

    private void OnContactChanged(object? sender, ContactChangedEventArgs e) => uiThread.Post(RefreshGroup);

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
            : group.Find(memberKey)?.Name.Value ?? string.Empty;
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
        CreatedByText = group.YouCreated ? UiText.GroupCreatedBy : Formats.With(Formats.GroupCreatedBy, NameOf(group, group.Record.Signer));

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

        for (int index = 0; index < all.Count; index++)
        {
            GroupChatMessage message = all[index];
            if (index < Messages.Count && Messages[index].Id.Equals(message.Id))
            {
                Messages[index].Update(message);
                continue;
            }

            string sender = group is null ? string.Empty : NameOf(group, message.AuthorKey);
            Messages.Insert(index, new GroupMessageItemViewModel(message, sender, timeZone, DeleteMessageAsync));
        }

        IsEmpty = Messages.Count == 0;
    }
}
