using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ConversationViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly Ed25519PublicKey contactKey;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private readonly Pages pages;
    private readonly TimeZoneInfo timeZone;
    private bool shown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DeleteChatQuestion))]
    private string contactName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    private string? waitingText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyNotCompared))]
    private string? keyNotComparedText;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string fingerprint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttemptText))]
    private string? attemptText;

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

    public ConversationViewModel(Messenger messenger, Ed25519PublicKey contactKey, IUiThread uiThread, INavigationService navigation, Pages pages)
        : this(messenger, contactKey, uiThread, navigation, pages, TimeZoneInfo.Local)
    {
    }

    public ConversationViewModel(
        Messenger messenger,
        Ed25519PublicKey contactKey,
        IUiThread uiThread,
        INavigationService navigation,
        Pages pages,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(timeZone);

        this.messenger = messenger;
        this.contactKey = contactKey;
        this.uiThread = uiThread;
        this.navigation = navigation;
        this.pages = pages;
        this.timeZone = timeZone;
        RefreshContact();
        RefreshMessages();
    }

    public override string Title => ContactName;

    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    public bool IsWaiting => WaitingText is not null;

    public bool IsKeyNotCompared => KeyNotComparedText is not null;

    public bool HasAttemptText => AttemptText is not null;

    public bool HasDeletedText => DeletedText is not null;

    public bool IsDeleteChatVisible => !IsConfirmingDeleteChat;

    public string DeleteChatQuestion => Formats.With(Formats.DeleteChatQuestion, ContactName);

    private bool IsNotDeletingChat => !IsDeletingChat;

    private bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(Draft);

    public override void OnShown()
    {
        shown = true;
        messenger.Contacts.Changed += OnContactChanged;
        messenger.Chats.Changed += OnChatChanged;
        messenger.Connections.Changed += OnConnectionChanged;
        RefreshContact();
        RefreshMessages();
        messenger.Chats.MarkRead(contactKey);
    }

    public override void OnHidden()
    {
        shown = false;
        messenger.Contacts.Changed -= OnContactChanged;
        messenger.Chats.Changed -= OnChatChanged;
        messenger.Connections.Changed -= OnConnectionChanged;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            SendResult result = await messenger.SendAsync(contactKey, Draft.TrimEnd(), cancellationToken);
            ErrorMessage = result.Outcome switch
            {
                SendOutcome.Queued => null,
                SendOutcome.Empty => null,
                SendOutcome.TooLong => UiText.ConversationTooLong,
                SendOutcome.NotSendable => UiText.ConversationNotSendable,
                SendOutcome.NotAContact => UiText.ConversationNotAContact,
                _ => Formats.With(Formats.NotAdded, ContactName),
            };

            if (result.IsQueued)
            {
                Draft = string.Empty;
                RefreshMessages();
            }
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
            await messenger.DeleteChatAsync(contactKey, cancellationToken);
            DeletedText = Formats.With(Formats.ChatDeleted, ContactName);
            IsConfirmingDeleteChat = false;
            RefreshMessages();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.ConversationDeleteChatFailed;
        }
        finally
        {
            IsDeletingChat = false;
        }
    }

    [RelayCommand]
    private void Compare()
    {
        if (messenger.Contacts.TryGet(contactKey, out Contact? contact))
        {
            navigation.Push(pages.VerifyContact(contact));
        }
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (!e.Contact.IdentityKey.Equals(contactKey))
        {
            return;
        }

        uiThread.Post(RefreshContact);
    }

    private async Task DeleteMessageAsync(MessageItemViewModel item, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            if (await messenger.DeleteWaitingAsync(contactKey, item.Id, cancellationToken))
            {
                DeletedText = Formats.With(Formats.MessageDeleted, ContactName);
            }
            else
            {
                ErrorMessage = UiText.ConversationMessageNotWaiting;
            }

            RefreshMessages();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.ConversationMessageDeleteFailed;
        }
    }

    private void OnChatChanged(object? sender, ChatChangedEventArgs e)
    {
        if (!e.ContactKey.Equals(contactKey) || (e.Added is null && e.Updated is null && e.Removed is null && !e.IsCleared))
        {
            return;
        }

        bool added = e.Added is not null;
        uiThread.Post(() =>
        {
            RefreshMessages();
            if (added && shown)
            {
                messenger.Chats.MarkRead(contactKey);
            }
        });
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (e.ContactKey is null || e.ContactKey.Equals(contactKey))
        {
            uiThread.Post(RefreshContact);
        }
    }

    private void RefreshContact()
    {
        if (!messenger.Contacts.TryGet(contactKey, out Contact? contact))
        {
            return;
        }

        ContactName = contact.Name.Value;
        Fingerprint = contact.Fingerprint.ToGroupedString();
        WaitingText = contact.IsAdded ? null : Formats.With(Formats.Waiting, ContactName);
        KeyNotComparedText = contact.KeyComparison == KeyComparison.NotCompared
            ? Formats.With(contact.Exchange == CodeExchange.InPerson ? Formats.KeyNotComparedInPerson : Formats.KeyNotCompared, ContactName)
            : null;
        Reachability reachability = messenger.Connections.ReachabilityOf(contactKey);
        StatusText = ReachabilityTexts.Status(contact, reachability);
        AttemptText = reachability == Reachability.NotReachable ? DialTexts.Failure(messenger.Connections.DialStatusOf(contactKey)) : null;
    }

    private void RefreshMessages()
    {
        IReadOnlyList<ChatMessage> all = messenger.Chats.MessagesWith(contactKey);
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
            ChatMessage message = all[index];
            if (index < Messages.Count && Messages[index].Id.Equals(message.Id))
            {
                Messages[index].Update(message);
                continue;
            }

            string sender = message.Direction == MessageDirection.Outgoing ? messenger.Identity.Name.Value : ContactName;
            Messages.Insert(index, new MessageItemViewModel(message, sender, timeZone, DeleteMessageAsync));
        }

        IsEmpty = Messages.Count == 0;
    }
}
