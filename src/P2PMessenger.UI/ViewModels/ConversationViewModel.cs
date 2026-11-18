using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Crypto;
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
    [NotifyPropertyChangedFor(nameof(Title))]
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
                SendOutcome.Sent => null,
                SendOutcome.Empty => null,
                SendOutcome.TooLong => UiText.ConversationTooLong,
                SendOutcome.NotSendable => UiText.ConversationNotSendable,
                SendOutcome.NotAContact => UiText.ConversationNotAContact,
                SendOutcome.WaitingForFirstConnection => Formats.With(Formats.NotAdded, ContactName),
                _ => Formats.With(Formats.NotConnected, ContactName),
            };

            if (result.IsSent)
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

    private void OnChatChanged(object? sender, ChatChangedEventArgs e)
    {
        if (e.ContactKey.Equals(contactKey) && e.Added is not null)
        {
            uiThread.Post(() =>
            {
                RefreshMessages();
                if (shown)
                {
                    messenger.Chats.MarkRead(contactKey);
                }
            });
        }
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
        KeyNotComparedText = contact.KeyComparison == KeyComparison.NotCompared ? Formats.With(Formats.KeyNotCompared, ContactName) : null;
        bool connected = messenger.Connections.IsConnected(contactKey);
        StatusText = !contact.IsAdded
            ? UiText.ContactWaiting
            : connected ? UiText.ContactConnected : UiText.ContactNotConnected;
        AttemptText = connected ? null : DialTexts.Failure(messenger.Connections.DialStatusOf(contactKey));
    }

    private void RefreshMessages()
    {
        IReadOnlyList<ChatMessage> all = messenger.Chats.MessagesWith(contactKey);
        for (int index = 0; index < all.Count; index++)
        {
            ChatMessage message = all[index];
            if (index < Messages.Count && Messages[index].Id.Equals(message.Id))
            {
                continue;
            }

            string sender = message.Direction == MessageDirection.Outgoing ? messenger.Identity.Name.Value : ContactName;
            Messages.Insert(index, new MessageItemViewModel(message, sender, timeZone));
        }

        IsEmpty = Messages.Count == 0;
    }
}
