using System.Text;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Delivery;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application;

public sealed class Messenger : IAsyncDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly Outbox outbox;
    private int disposed;

    private Messenger(OwnIdentity identity, ContactBook contacts, ChatLog chats, ConnectionService connections, Outbox outbox, TimeProvider timeProvider)
    {
        Identity = identity;
        Contacts = contacts;
        Chats = chats;
        Connections = connections;
        this.outbox = outbox;
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
        string? problem = setup.Delivery.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(setup));
        }

        IReadOnlyList<Contact> contacts = await setup.ContactStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ChatMessage> messages = await setup.ChatStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Ed25519PublicKey, long> sentSequences = await setup.ChatStore.LoadSentSequencesAsync(cancellationToken).ConfigureAwait(false);

        ContactBook contactBook = new(identity, setup.ContactStore, contacts);
        ChatLog chatLog = new(setup.ChatStore, setup.SeenMessageIds, setup.TimeProvider, messages, sentSequences);
        ConnectionService connections = new(identity, contactBook, chatLog, setup.Connection, setup.TimeProvider, setup.Loggers);
        IWakeUpScheduler scheduler = setup.WakeUps ?? new TimerWakeUpScheduler(setup.TimeProvider);
        Outbox outbox = new(chatLog, connections, scheduler, setup.Delivery, setup.TimeProvider, setup.Loggers.CreateLogger<Outbox>());
        Messenger messenger = new(identity, contactBook, chatLog, connections, outbox, setup.TimeProvider);
        try
        {
            await connections.ListenAsync(setup.ListenAddress, setup.ListenPort, cancellationToken).ConfigureAwait(false);
            if (setup.Discovery is { } discovery)
            {
                connections.StartDiscovery(discovery);
            }
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

        DateTimeOffset now = timeProvider.GetUtcNow();
        long sequence = Chats.NextSequence(contactKey);
        if (!TextMessage.TryCreate(MessageId.Create(), sequence, now, text, out TextMessage? message))
        {
            return new SendResult(SendOutcome.NotSendable, null);
        }

        ChatMessage recorded = await Chats.RecordOutgoingAsync(contactKey, message, now, cancellationToken).ConfigureAwait(false);
        outbox.Request(contactKey);
        return new SendResult(SendOutcome.Queued, recorded);
    }

    public Task<int> DeleteChatAsync(Ed25519PublicKey contactKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        return Chats.DeleteChatAsync(contactKey, cancellationToken);
    }

    public Task<bool> DeleteWaitingAsync(Ed25519PublicKey contactKey, MessageId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(id);

        return Chats.DeleteWaitingAsync(contactKey, id, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await outbox.DisposeAsync().ConfigureAwait(false);
        await Connections.DisposeAsync().ConfigureAwait(false);
        Chats.Dispose();
        Contacts.Dispose();
    }
}
