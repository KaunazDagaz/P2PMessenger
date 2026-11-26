using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Groups;

namespace P2PMessenger.Application.Background;

public sealed class MessageAlerts : IDisposable
{
    private readonly Messenger messenger;
    private readonly IMessageNotifier notifier;
    private readonly BackgroundSettings settings;
    private readonly Func<bool> isAppVisible;
    private int disposed;

    public MessageAlerts(Messenger messenger, IMessageNotifier notifier, BackgroundSettings settings, Func<bool> isAppVisible)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isAppVisible);

        this.messenger = messenger;
        this.notifier = notifier;
        this.settings = settings;
        this.isAppVisible = isAppVisible;
        messenger.Chats.Changed += OnChatChanged;
        messenger.GroupChats.Changed += OnGroupChatChanged;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            messenger.Chats.Changed -= OnChatChanged;
            messenger.GroupChats.Changed -= OnGroupChatChanged;
        }
    }

    private void OnChatChanged(object? sender, ChatChangedEventArgs e)
    {
        if (Volatile.Read(ref disposed) != 0
            || e.Added is not { Direction: MessageDirection.Incoming } message
            || isAppVisible()
            || !notifier.IsAllowed
            || !messenger.Contacts.TryGet(e.ContactKey, out Contact? contact))
        {
            return;
        }

        notifier.Show(new MessageNotice(e.ContactKey, contact.Name.Value, settings.ShowPreviews ? message.Text : null));
    }

    private void OnGroupChatChanged(object? sender, GroupChatChangedEventArgs e)
    {
        if (Volatile.Read(ref disposed) != 0
            || e.Added is not { Direction: MessageDirection.Incoming } message
            || isAppVisible()
            || !notifier.IsAllowed
            || !messenger.Groups.TryGet(e.GroupId, out Group? group))
        {
            return;
        }

        string author = messenger.Contacts.TryGet(message.AuthorKey, out Contact? contact)
            ? contact.Name.Value
            : group.Find(message.AuthorKey)?.Name.Value ?? string.Empty;
        notifier.ShowGroup(new GroupMessageNotice(group.Id, group.Name.Value, author, settings.ShowPreviews ? message.Text : null));
    }
}
