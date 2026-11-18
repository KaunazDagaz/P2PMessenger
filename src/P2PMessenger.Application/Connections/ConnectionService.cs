using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tcp;

namespace P2PMessenger.Application.Connections;

public sealed class ConnectionService : IAsyncDisposable
{
    private readonly OwnIdentity own;
    private readonly ContactBook contacts;
    private readonly ChatLog chats;
    private readonly ConnectionOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggers;
    private readonly KnownContacts known = new();
    private readonly ConnectionManager manager;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, SessionContact> sessionContacts = [];
    private readonly List<SessionContact> retired = [];
    private readonly Dictionary<Ed25519PublicKey, IPEndPoint> addresses = [];
    private readonly Dictionary<Ed25519PublicKey, Dialer> dialers = [];
    private TcpConnectionListener? listener;
    private bool disposed;

    internal ConnectionService(
        OwnIdentity own,
        ContactBook contacts,
        ChatLog chats,
        ConnectionOptions options,
        TimeProvider timeProvider,
        ILoggerFactory loggers)
    {
        this.own = own;
        this.contacts = contacts;
        this.chats = chats;
        this.options = options;
        this.timeProvider = timeProvider;
        this.loggers = loggers;
        manager = new ConnectionManager(own.Keys, known, new Observer(this), options, timeProvider, loggers.CreateLogger<ConnectionManager>());

        contacts.Changed += OnContactChanged;
        foreach (Contact contact in contacts.All)
        {
            Know(contact);
        }
    }

    public event EventHandler<ConnectionChangedEventArgs>? Changed;

    public int? ListeningPort
    {
        get
        {
            lock (gate)
            {
                return listener?.LocalEndpoint.Port;
            }
        }
    }

    public ListenResult LastListenResult { get; private set; } = ListenResult.Failed;

    public int? LastRequestedPort { get; private set; }

    public bool IsConnected(Ed25519PublicKey contactKey) => manager.TryGetConnection(contactKey, out _);

    public bool TryGetAddress(Ed25519PublicKey contactKey, [NotNullWhen(true)] out IPEndPoint? address)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return addresses.TryGetValue(contactKey, out address);
        }
    }

    public DialStatus DialStatusOf(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return dialers.TryGetValue(contactKey, out Dialer? dialer) ? dialer.Status : DialStatus.None;
        }
    }

    public async Task<ListenResult> ListenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return ListenResult.InvalidPort;
        }

        TcpConnectionListener? previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previous = listener;
            listener = null;
        }

        if (previous is not null)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ListenResult result;
        TcpConnectionListener? started = null;
        try
        {
            started = TcpConnectionListener.Start(manager, new IPEndPoint(address, port), loggers.CreateLogger<TcpConnectionListener>());
            result = ListenResult.Listening;
        }
        catch (SocketException exception)
        {
            result = exception.SocketErrorCode == SocketError.AddressAlreadyInUse ? ListenResult.PortInUse : ListenResult.Failed;
        }

        bool keep;
        lock (gate)
        {
            keep = !disposed;
            if (keep)
            {
                listener = started;
                LastListenResult = result;
                LastRequestedPort = port;
            }
        }

        if (!keep && started is not null)
        {
            await started.DisposeAsync().ConfigureAwait(false);
            return ListenResult.Failed;
        }

        RaiseChanged(null);
        return result;
    }

    public bool SetAddress(Ed25519PublicKey contactKey, IPEndPoint? address)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        Dialer? previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!sessionContacts.ContainsKey(contactKey))
            {
                return false;
            }

            if (address is null)
            {
                addresses.Remove(contactKey);
            }
            else
            {
                addresses[contactKey] = address;
            }

            previous = RestartDialerLocked(contactKey);
        }

        previous?.Retire();
        RaiseChanged(contactKey);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        TcpConnectionListener? stopping;
        Dialer[] stoppingDialers;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopping = listener;
            listener = null;
            stoppingDialers = [.. dialers.Values];
            dialers.Clear();
        }

        contacts.Changed -= OnContactChanged;
        foreach (Dialer dialer in stoppingDialers)
        {
            dialer.Cancel();
        }

        if (stopping is not null)
        {
            await stopping.DisposeAsync().ConfigureAwait(false);
        }

        await manager.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(stoppingDialers.Select(static dialer => dialer.Loop)).ConfigureAwait(false);
        foreach (Dialer dialer in stoppingDialers)
        {
            dialer.Dispose();
        }

        lock (gate)
        {
            foreach (SessionContact contact in sessionContacts.Values.Concat(retired))
            {
                contact.Dispose();
            }

            sessionContacts.Clear();
            retired.Clear();
        }
    }

    internal Task<bool> SendAsync(Ed25519PublicKey contactKey, SessionMessage message, CancellationToken cancellationToken) =>
        manager.SendAsync(contactKey, message, cancellationToken);

    private static TimeSpan Jittered(TimeSpan delay) => delay * (0.8 + (RandomNumberGenerator.GetInt32(0, 401) / 1000.0));

    private static TimeSpan Doubled(TimeSpan delay, TimeSpan longest)
    {
        TimeSpan doubled = delay * 2;
        return doubled < longest ? doubled : longest;
    }

    private static DialStatus StatusOf(ConnectFailure failure) => failure switch
    {
        ConnectFailure.Unreachable => DialStatus.Unreachable,
        ConnectFailure.TimedOut => DialStatus.TimedOut,
        _ => DialStatus.Refused,
    };

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        switch (e.Change)
        {
            case ContactChange.Added:
                Know(e.Contact);
                break;
            case ContactChange.KeysReplaced:
                Replace(e.Contact);
                break;
        }
    }

    private void Know(Contact contact)
    {
        if (!SessionContact.TryCreate(own.Keys, contact.Code, out SessionContact? sessionContact))
        {
            return;
        }

        lock (gate)
        {
            if (disposed || !known.TryAdd(sessionContact))
            {
                sessionContact.Dispose();
                return;
            }

            sessionContacts[contact.IdentityKey] = sessionContact;
        }
    }

    private void Replace(Contact contact)
    {
        if (!SessionContact.TryCreate(own.Keys, contact.Code, out SessionContact? sessionContact))
        {
            return;
        }

        Dialer? previous;
        lock (gate)
        {
            if (disposed)
            {
                sessionContact.Dispose();
                return;
            }

            if (known.TryRemove(contact.IdentityKey, out SessionContact? old))
            {
                retired.Add(old);
            }

            sessionContacts.Remove(contact.IdentityKey);
            if (known.TryAdd(sessionContact))
            {
                sessionContacts[contact.IdentityKey] = sessionContact;
            }
            else
            {
                sessionContact.Dispose();
            }

            previous = RestartDialerLocked(contact.IdentityKey);
        }

        previous?.Retire();
        if (manager.TryGetConnection(contact.IdentityKey, out ContactConnection? connection))
        {
            connection.Close();
        }

        RaiseChanged(contact.IdentityKey);
    }

    private Dialer? RestartDialerLocked(Ed25519PublicKey contactKey)
    {
        dialers.Remove(contactKey, out Dialer? previous);
        if (addresses.TryGetValue(contactKey, out IPEndPoint? address)
            && sessionContacts.TryGetValue(contactKey, out SessionContact? sessionContact))
        {
            Dialer dialer = new(address);
            dialers.Add(contactKey, dialer);
            dialer.Loop = Task.Run(() => DialLoopAsync(contactKey, sessionContact, dialer), CancellationToken.None);
        }

        return previous;
    }

    private async Task DialLoopAsync(Ed25519PublicKey contactKey, SessionContact contact, Dialer dialer)
    {
        CancellationToken stop = dialer.Token;
        TcpDialer tcp = new(dialer.Address);
        TimeSpan delay = options.ReconnectDelayMin;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (manager.TryGetConnection(contactKey, out ContactConnection? current))
                {
                    SetStatus(contactKey, dialer, DialStatus.None);
                    await current.Completion.WaitAsync(stop).ConfigureAwait(false);
                    delay = options.ReconnectDelayMin;
                    await Task.Delay(Jittered(delay), timeProvider, stop).ConfigureAwait(false);
                    continue;
                }

                SetStatus(contactKey, dialer, DialStatus.Connecting);
                ConnectResult result = await manager.ConnectAsync(contact, tcp, stop).ConfigureAwait(false);
                if (result.IsConnected)
                {
                    delay = options.ReconnectDelayMin;
                    continue;
                }

                if (result.Failure == ConnectFailure.ShutDown)
                {
                    return;
                }

                SetStatus(contactKey, dialer, StatusOf(result.Failure));
                await Task.Delay(Jittered(delay), timeProvider, stop).ConfigureAwait(false);
                delay = Doubled(delay, options.ReconnectDelayMax);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void SetStatus(Ed25519PublicKey contactKey, Dialer dialer, DialStatus status)
    {
        lock (gate)
        {
            if (dialer.Status == status)
            {
                return;
            }

            dialer.Status = status;
        }

        RaiseChanged(contactKey);
    }

    private void RaiseChanged(Ed25519PublicKey? contactKey) => Changed?.Invoke(this, new ConnectionChangedEventArgs(contactKey));

    private sealed class Dialer : IDisposable
    {
        private readonly CancellationTokenSource stop = new();

        public Dialer(IPEndPoint address)
        {
            Address = address;
            Token = stop.Token;
        }

        public IPEndPoint Address { get; }

        public CancellationToken Token { get; }

        public Task Loop { get; set; } = Task.CompletedTask;

        public DialStatus Status { get; set; }

        public void Cancel() => stop.Cancel();

        public void Retire()
        {
            Cancel();
            _ = Loop.ContinueWith(_ => Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose() => stop.Dispose();
    }

    private sealed class Observer : IConnectionObserver
    {
        private readonly ConnectionService service;

        public Observer(ConnectionService service)
        {
            this.service = service;
        }

        public async ValueTask ConnectedAsync(ContactConnection connection, CancellationToken cancellationToken)
        {
            SessionContact peer = connection.Contact;
            await service.contacts.ConfirmFirstConnectionAsync(peer.IdentityKey, peer.StaticKey, cancellationToken).ConfigureAwait(false);
            service.RaiseChanged(peer.IdentityKey);
        }

        public async ValueTask MessageReceivedAsync(ContactConnection connection, SessionMessage message, CancellationToken cancellationToken)
        {
            if (message is TextMessage text)
            {
                await service.chats.ReceiveAsync(connection.Contact.IdentityKey, text, cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask DisconnectedAsync(ContactConnection connection, CancellationToken cancellationToken)
        {
            service.RaiseChanged(connection.Contact.IdentityKey);
            return ValueTask.CompletedTask;
        }
    }
}
