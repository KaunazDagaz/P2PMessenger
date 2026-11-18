using System.Text;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application;

public sealed class Messenger : IAsyncDisposable
{
    private readonly TimeProvider timeProvider;
    private int disposed;

    private Messenger(OwnIdentity identity, ContactBook contacts, ChatLog chats, ConnectionService connections, TimeProvider timeProvider)
    {
        Identity = identity;
        Contacts = contacts;
        Chats = chats;
        Connections = connections;
        this.timeProvider = timeProvider;
    }

    public OwnIdentity Identity { get; }

    public ContactBook Contacts { get; }

    public ChatLog Chats { get; }

    public ConnectionService Connections { get; }

    public static async Task<Messenger> StartAsync(OwnIdentity identity, MessengerSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(setup);

        IReadOnlyList<Contact> contacts = await setup.ContactStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ChatMessage> messages = await setup.ChatStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        ContactBook contactBook = new(identity, setup.ContactStore, contacts);
        ChatLog chatLog = new(setup.ChatStore, setup.SeenMessageIds, setup.TimeProvider, messages);
        ConnectionService connections = new(identity, contactBook, chatLog, setup.Connection, setup.TimeProvider, setup.Loggers);
        Messenger messenger = new(identity, contactBook, chatLog, connections, setup.TimeProvider);
        try
        {
            await connections.ListenAsync(setup.ListenAddress, setup.ListenPort, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await messenger.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return messenger;
    }

    public async Task<SendResult> SendAsync(Ed25519PublicKey contactKey, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new SendResult(SendOutcome.Empty, null);
        }

        if (!TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, text, out _))
        {
            bool tooLong = Encoding.UTF8.GetByteCount(text) > TextMessage.MaxUtf8Length;
            return new SendResult(tooLong ? SendOutcome.TooLong : SendOutcome.NotSendable, null);
        }

        if (!Contacts.TryGet(contactKey, out Contact? contact))
        {
            return new SendResult(SendOutcome.NotAContact, null);
        }

        if (!contact.IsAdded)
        {
            return new SendResult(SendOutcome.WaitingForFirstConnection, null);
        }

        if (!Connections.IsConnected(contactKey))
        {
            return new SendResult(SendOutcome.NotConnected, null);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        long sequence = Chats.NextSequence(contactKey);
        if (!TextMessage.TryCreate(MessageId.Create(), sequence, now, text, out TextMessage? message))
        {
            return new SendResult(SendOutcome.NotSendable, null);
        }

        if (!await Connections.SendAsync(contactKey, message, cancellationToken).ConfigureAwait(false))
        {
            return new SendResult(SendOutcome.NotConnected, null);
        }

        ChatMessage recorded = await Chats.RecordOutgoingAsync(contactKey, message, now, cancellationToken).ConfigureAwait(false);
        return new SendResult(SendOutcome.Sent, recorded);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await Connections.DisposeAsync().ConfigureAwait(false);
        Chats.Dispose();
        Contacts.Dispose();
    }
}
