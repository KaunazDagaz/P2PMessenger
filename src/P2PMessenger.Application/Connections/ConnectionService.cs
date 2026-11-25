using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Discovery;
using P2PMessenger.Transport.Tcp;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Connections;

public sealed class ConnectionService : IAsyncDisposable
{
    public const int RandomPortMin = 49152;
    public const int RandomPortMax = 65535;

    private const int RandomPortAttempts = 32;

    private readonly OwnIdentity own;
    private readonly ContactBook contacts;
    private readonly ChatLog chats;
    private readonly ConnectionOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggers;
    private readonly IFailureSink? failures;
    private readonly KnownContacts known = new();
    private readonly ConnectionManager manager;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, SessionContact> sessionContacts = [];
    private readonly List<SessionContact> retired = [];
    private readonly Dictionary<Ed25519PublicKey, NearbyPeer> nearby = [];
    private readonly List<Dialer> finishedDialers = [];
    private readonly TorConnections tor;
    private TcpConnectionListener? listener;
    private LocalNetworkDiscovery? discovery;
    private TimeSpan heardLifetime = new DiscoveryOptions().HeardLifetime;
    private bool disposed;

    internal ConnectionService(
        OwnIdentity own,
        ContactBook contacts,
        ChatLog chats,
        ConnectionOptions options,
        TimeProvider timeProvider,
        ILoggerFactory loggers,
        IFailureSink? failures = null)
    {
        this.own = own;
        this.contacts = contacts;
        this.chats = chats;
        this.options = options;
        this.timeProvider = timeProvider;
        this.loggers = loggers;
        this.failures = failures;
        manager = new ConnectionManager(own.Keys, known, new Observer(this), options, timeProvider, loggers.CreateLogger<ConnectionManager>(), failures);
        tor = new TorConnections(manager, options, timeProvider, loggers.CreateLogger<OnionDialer>(), contactKey => RaiseChanged(contactKey));

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

    public DiscoveryState DiscoveryState
    {
        get
        {
            lock (gate)
            {
                return discovery?.State ?? DiscoveryState.NotStarted;
            }
        }
    }

    public int? DiscoveryPort { get; private set; }

    public NetworkSnapshot? Network
    {
        get
        {
            lock (gate)
            {
                return discovery?.Network;
            }
        }
    }

    public bool UsesTor => tor.Proxy is not null;

    internal IPEndPoint? TorProxy => tor.Proxy;

    public bool IsConnected(Ed25519PublicKey contactKey) => manager.TryGetConnection(contactKey, out _);

    public Reachability ReachabilityOf(Ed25519PublicKey contactKey)
    {
        if (!manager.TryGetConnection(contactKey, out ContactConnection? connection))
        {
            return Reachability.NotReachable;
        }

        return connection.Route == ConnectionRoute.Tor ? Reachability.Tor : Reachability.Nearby;
    }

    public TorDialStatus TorStatusOf(Ed25519PublicKey contactKey) => tor.StatusOf(contactKey);

    public bool IsHeard(Ed25519PublicKey contactKey) => TryGetHeardEndpoint(contactKey, out _);

    public bool TryGetHeardEndpoint(Ed25519PublicKey contactKey, [NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            if (nearby.TryGetValue(contactKey, out NearbyPeer? peer) && IsFresh(peer) && peer.Endpoint is { } heard)
            {
                endpoint = heard;
                return true;
            }
        }

        endpoint = null;
        return false;
    }

    public DialStatus DialStatusOf(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return nearby.TryGetValue(contactKey, out NearbyPeer? peer) && peer.Dialer is { } dialer ? dialer.Status : DialStatus.None;
        }
    }

    public async Task<ListenResult> ListenAsync(IPAddress address, int? port, CancellationToken cancellationToken)
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
        (TcpConnectionListener? started, ListenResult result) = port is { } chosen ? Start(address, chosen) : StartOnRandomPort(address);

        bool keep;
        LocalNetworkDiscovery? running;
        lock (gate)
        {
            keep = !disposed;
            running = discovery;
            if (keep)
            {
                listener = started;
                LastListenResult = result;
            }
        }

        if (!keep && started is not null)
        {
            await started.DisposeAsync().ConfigureAwait(false);
            return ListenResult.Failed;
        }

        running?.SetAnnouncedPort(started?.LocalEndpoint.Port);
        RaiseChanged(null);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        TcpConnectionListener? stopping;
        LocalNetworkDiscovery? stoppingDiscovery;
        Dialer[] activeDialers;
        Dialer[] doneDialers;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopping = listener;
            listener = null;
            stoppingDiscovery = discovery;
            activeDialers = [.. nearby.Values.Select(static peer => peer.Dialer).OfType<Dialer>()];
            doneDialers = [.. finishedDialers];
            foreach (NearbyPeer peer in nearby.Values)
            {
                peer.Dialer = null;
            }

            finishedDialers.Clear();
        }

        contacts.Changed -= OnContactChanged;
        if (stoppingDiscovery is not null)
        {
            await stoppingDiscovery.DisposeAsync().ConfigureAwait(false);
        }

        await tor.DisposeAsync().ConfigureAwait(false);

        foreach (Dialer dialer in activeDialers)
        {
            dialer.Cancel();
        }

        if (stopping is not null)
        {
            await stopping.DisposeAsync().ConfigureAwait(false);
        }

        await manager.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(activeDialers.Select(static dialer => dialer.Loop)).ConfigureAwait(false);
        foreach (Dialer dialer in activeDialers.Concat(doneDialers))
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

    internal OnionListener StartOnionListener(string? socketPath) =>
        OnionListener.Start(manager, socketPath, loggers.CreateLogger<OnionListener>());

    internal void UseTorProxy(IPEndPoint? proxy) => tor.UseProxy(proxy);

    internal void StartDiscovery(DiscoverySetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        IDiscoveryLink link = setup.Links(setup.Options);
        LocalNetworkDiscovery started;
        try
        {
            started = new LocalNetworkDiscovery(
                known,
                setup.Interfaces,
                link,
                new DiscoveryObserver(this),
                setup.Options,
                timeProvider,
                loggers.CreateLogger<LocalNetworkDiscovery>(),
                failures);
        }
        catch
        {
            link.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (discovery is not null)
            {
                throw new InvalidOperationException("Discovery has already been started.");
            }

            discovery = started;
            heardLifetime = setup.Options.HeardLifetime;
            DiscoveryPort = setup.Options.Port;
        }

        started.Start(ListeningPort);
        RaiseChanged(null);
    }

    internal bool TryGetConnection(Ed25519PublicKey contactKey, [NotNullWhen(true)] out ContactConnection? connection) =>
        manager.TryGetConnection(contactKey, out connection);

    private static DialStatus StatusOf(ConnectFailure failure) => failure switch
    {
        ConnectFailure.Unreachable => DialStatus.Unreachable,
        ConnectFailure.TimedOut => DialStatus.TimedOut,
        _ => DialStatus.Refused,
    };

    private (TcpConnectionListener? Listener, ListenResult Result) Start(IPAddress address, int port)
    {
        try
        {
            return (TcpConnectionListener.Start(manager, new IPEndPoint(address, port), loggers.CreateLogger<TcpConnectionListener>()), ListenResult.Listening);
        }
        catch (SocketException exception)
        {
            return (null, ListenPorts.ResultOf(exception.SocketErrorCode));
        }
    }

    private (TcpConnectionListener? Listener, ListenResult Result) StartOnRandomPort(IPAddress address) =>
        ListenPorts.StartOnRandomPort(
            static () => RandomNumberGenerator.GetInt32(RandomPortMin, RandomPortMax + 1),
            port => TcpConnectionListener.Start(manager, new IPEndPoint(address, port), loggers.CreateLogger<TcpConnectionListener>()),
            RandomPortAttempts);

    private bool IsFresh(NearbyPeer peer) =>
        peer.Endpoint is not null && timeProvider.GetElapsedTime(peer.HeardAt) < heardLifetime;

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        switch (e.Change)
        {
            case ContactChange.Added:
                Know(e.Contact);
                AnnounceSoon();
                break;
            case ContactChange.KeysReplaced:
                Replace(e.Contact);
                AnnounceSoon();
                break;
        }
    }

    private void AnnounceSoon()
    {
        LocalNetworkDiscovery? running;
        lock (gate)
        {
            running = discovery;
        }

        running?.AnnounceSoon();
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

        tor.Know(sessionContact, contact.Code.OnionAddress);
    }

    private void Replace(Contact contact)
    {
        if (!SessionContact.TryCreate(own.Keys, contact.Code, out SessionContact? sessionContact))
        {
            return;
        }

        Dialer? previous = null;
        bool kept;
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
            kept = known.TryAdd(sessionContact);
            if (kept)
            {
                sessionContacts[contact.IdentityKey] = sessionContact;
            }
            else
            {
                sessionContact.Dispose();
            }

            if (nearby.Remove(contact.IdentityKey, out NearbyPeer? peer))
            {
                previous = peer.Dialer;
            }
        }

        previous?.Retire();
        if (kept)
        {
            tor.Know(sessionContact, contact.Code.OnionAddress);
        }

        if (manager.TryGetConnection(contact.IdentityKey, out ContactConnection? connection))
        {
            connection.Close();
        }

        RaiseChanged(contact.IdentityKey);
    }

    private void OnHeard(SessionContact contact, IPEndPoint endpoint)
    {
        Ed25519PublicKey contactKey = contact.IdentityKey;
        bool firstHeard;
        lock (gate)
        {
            if (disposed || !sessionContacts.TryGetValue(contactKey, out SessionContact? current) || !ReferenceEquals(current, contact))
            {
                return;
            }

            if (!nearby.TryGetValue(contactKey, out NearbyPeer? peer))
            {
                peer = new NearbyPeer();
                nearby.Add(contactKey, peer);
            }

            firstHeard = !IsFresh(peer) || peer.Dialer is null;
            peer.Endpoint = endpoint;
            peer.HeardAt = timeProvider.GetTimestamp();
            if (peer.Dialer is null)
            {
                DisposeFinishedDialersLocked();
                Dialer dialer = new();
                peer.Dialer = dialer;
                dialer.Loop = Task.Run(() => DialLoopAsync(contactKey, contact, dialer), CancellationToken.None);
            }
        }

        if (firstHeard)
        {
            RaiseChanged(contactKey);
        }
    }

    private void DisposeFinishedDialersLocked()
    {
        for (int index = finishedDialers.Count - 1; index >= 0; index--)
        {
            Dialer finished = finishedDialers[index];
            if (finished.Loop.IsCompleted)
            {
                finishedDialers.RemoveAt(index);
                finished.Dispose();
            }
        }
    }

    private bool TryTakeEndpoint(Ed25519PublicKey contactKey, Dialer dialer, [NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        lock (gate)
        {
            endpoint = null;
            if (!nearby.TryGetValue(contactKey, out NearbyPeer? peer) || !ReferenceEquals(peer.Dialer, dialer))
            {
                return false;
            }

            if (IsFresh(peer))
            {
                endpoint = peer.Endpoint!;
                return true;
            }

            peer.Dialer = null;
            finishedDialers.Add(dialer);
            return false;
        }
    }

    private async Task DialLoopAsync(Ed25519PublicKey contactKey, SessionContact contact, Dialer dialer)
    {
        CancellationToken stop = dialer.Token;
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
                    await Task.Delay(Delays.Jittered(delay), timeProvider, stop).ConfigureAwait(false);
                    continue;
                }

                if (!TryTakeEndpoint(contactKey, dialer, out IPEndPoint? endpoint))
                {
                    RaiseChanged(contactKey);
                    return;
                }

                SetConnecting(contactKey, dialer);
                ConnectResult result = await manager.ConnectAsync(contact, new TcpDialer(endpoint), stop).ConfigureAwait(false);
                if (result.IsConnected)
                {
                    SetStatus(contactKey, dialer, DialStatus.None);
                    delay = options.ReconnectDelayMin;
                    continue;
                }

                if (result.Failure == ConnectFailure.ShutDown)
                {
                    return;
                }

                SetStatus(contactKey, dialer, StatusOf(result.Failure));
                await Task.Delay(Delays.Jittered(delay), timeProvider, stop).ConfigureAwait(false);
                delay = Delays.Doubled(delay, options.ReconnectDelayMax);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void SetConnecting(Ed25519PublicKey contactKey, Dialer dialer)
    {
        lock (gate)
        {
            if (dialer.Status != DialStatus.None)
            {
                return;
            }

            dialer.Status = DialStatus.Connecting;
        }

        RaiseChanged(contactKey);
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

    private sealed class NearbyPeer
    {
        public IPEndPoint? Endpoint { get; set; }

        public long HeardAt { get; set; }

        public Dialer? Dialer { get; set; }
    }

    private sealed class Dialer : IDisposable
    {
        private readonly CancellationTokenSource stop = new();

        public Dialer()
        {
            Token = stop.Token;
        }

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

    private sealed class DiscoveryObserver : IDiscoveryObserver
    {
        private readonly ConnectionService service;

        public DiscoveryObserver(ConnectionService service)
        {
            this.service = service;
        }

        public void Heard(SessionContact contact, IPEndPoint endpoint) => service.OnHeard(contact, endpoint);

        public void NetworkChanged(NetworkSnapshot network) => service.RaiseChanged(null);
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
            Ed25519PublicKey contactKey = connection.Contact.IdentityKey;
            switch (message)
            {
                case TextMessage text:
                    await service.chats.ReceiveAsync(contactKey, text, cancellationToken).ConfigureAwait(false);
                    _ = connection.SendAsync(Acknowledgement.Create(text.Id), CancellationToken.None);
                    break;
                case Acknowledgement acknowledgement:
                    await service.chats.MarkReceivedAsync(contactKey, acknowledgement.Id, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        public ValueTask DisconnectedAsync(ContactConnection connection, CancellationToken cancellationToken)
        {
            service.RaiseChanged(connection.Contact.IdentityKey);
            return ValueTask.CompletedTask;
        }
    }
}
