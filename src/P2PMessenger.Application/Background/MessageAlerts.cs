using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;

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
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            messenger.Chats.Changed -= OnChatChanged;
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
}
