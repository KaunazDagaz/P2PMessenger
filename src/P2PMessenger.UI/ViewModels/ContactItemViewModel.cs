using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed class ContactItemViewModel : ViewModelBase, IChatItem
{
    public ContactItemViewModel(Contact contact, Messenger messenger, Action<Contact> open)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(open);

        Contact = contact;
        Name = contact.Name.Value;
        Fingerprint = contact.Fingerprint.ToGroupedString();
        Reachability reachability = messenger.Connections.ReachabilityOf(contact.IdentityKey);
        StatusText = ReachabilityTexts.Status(contact, reachability);
        IsOnline = contact.IsAdded && reachability != Reachability.NotReachable;
        IsPending = !contact.IsAdded;
        KeyText = contact.KeyComparison == KeyComparison.CheckedByYou ? UiText.ContactKeyChecked : UiText.ContactKeyNotCompared;
        int unread = messenger.Chats.UnreadCount(contact.IdentityKey);
        UnreadText = unread > 0 ? Formats.With(Formats.Unread, unread) : null;
        LastActivity = messenger.Chats.MessagesWith(contact.IdentityKey).Max(static message => (DateTimeOffset?)message.RecordedAt);
        OpenCommand = new RelayCommand(() => open(contact));
    }

    public Contact Contact { get; }

    public DateTimeOffset? LastActivity { get; }

    public bool IsSelected { get; init; }

    public string Name { get; }

    public string Fingerprint { get; }

    public string StatusText { get; }

    public bool IsOnline { get; }

    public bool IsPending { get; }

    public string KeyText { get; }

    public string? UnreadText { get; }

    public bool HasUnread => UnreadText is not null;

    public IRelayCommand OpenCommand { get; }
}
