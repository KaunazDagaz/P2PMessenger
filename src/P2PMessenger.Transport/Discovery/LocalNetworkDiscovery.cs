using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Discovery;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Admission;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Discovery;

public sealed class LocalNetworkDiscovery : IAsyncDisposable
{
    private const int ReceiveBufferLength = 512;
    private const int NonceCapacity = 8192;

    private readonly IKnownContacts contacts;
    private readonly ILocalInterfaceSource interfaces;
    private readonly IDiscoveryLink link;
    private readonly IDiscoveryObserver observer;
    private readonly DiscoveryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private readonly RecentNonces ownNonces;
    private readonly RecentNonces seenNonces;
    private readonly TokenBucket screenings;
    private readonly Dictionary<IPAddress, TokenBucket> sources = [];
    private readonly Dictionary<IPAddress, SocketError> lastSendErrors = [];
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private NetworkSnapshot network = NetworkSnapshot.None;
    private bool networkKnown;
    private int? announcedPort;
    private long lastRecognized;
    private long? lastSweep;
    private long? lastRefresh;
    private long droppedScreenings;
    private bool sweeping;
    private DiscoveryState state;
    private Task receiveLoop = Task.CompletedTask;
    private Task announceLoop = Task.CompletedTask;
    private Task sweep = Task.CompletedTask;

    public LocalNetworkDiscovery(
        IKnownContacts contacts,
        ILocalInterfaceSource interfaces,
        IDiscoveryLink link,
        IDiscoveryObserver observer,
        DiscoveryOptions options,
        TimeProvider timeProvider,
        ILogger<LocalNetworkDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        string? problem = options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        this.contacts = contacts;
        this.interfaces = interfaces;
        this.link = link;
        this.observer = observer;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        long frequency = timeProvider.TimestampFrequency;
        ownNonces = new RecentNonces(options.NonceMemory, frequency, NonceCapacity);
        seenNonces = new RecentNonces(options.NonceMemory, frequency, NonceCapacity);
        screenings = new TokenBucket(options.ScreeningsPerSecond, options.ScreeningBurst, timeProvider.GetTimestamp());
        lastRecognized = timeProvider.GetTimestamp();
    }

    public DiscoveryState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public NetworkSnapshot? Network
    {
        get
        {
            lock (gate)
            {
                return networkKnown ? network : null;
            }
        }
    }

    public int? AnnouncedPort
    {
        get
        {
            lock (gate)
            {
                return announcedPort;
            }
        }
    }

    public void Start(int? port)
    {
        CheckPort(port);
        lock (gate)
        {
            if (state != DiscoveryState.NotStarted)
            {
                throw new InvalidOperationException("Discovery has already been started.");
            }

            state = DiscoveryState.Running;
            announcedPort = port;
            lastRecognized = timeProvider.GetTimestamp();
        }

        SocketError opened = link.Open();
        int discoveryPort = options.Port;
        if (opened == SocketError.Success)
        {
            logger.DiscoveryStarted(discoveryPort);
        }
        else
        {
            lock (gate)
            {
                state = DiscoveryState.Unavailable;
            }

            logger.DiscoveryUnavailable(discoveryPort, opened);
        }

        CancellationToken token = stopping.Token;
        RefreshInterfaces();
        if (opened == SocketError.Success)
        {
            receiveLoop = Task.Run(() => ReceiveLoopAsync(token), CancellationToken.None);
        }

        announceLoop = Task.Run(() => AnnounceLoopAsync(token), CancellationToken.None);
    }

    public void SetAnnouncedPort(int? port)
    {
        CheckPort(port);
        lock (gate)
        {
            announcedPort = port;
        }

        AnnounceSoon();
    }

    public void AnnounceSoon()
    {
        lock (gate)
        {
            if (state is DiscoveryState.NotStarted or DiscoveryState.Stopped || wake.CurrentCount > 0)
            {
                return;
            }

            wake.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] loops;
        lock (gate)
        {
            if (state == DiscoveryState.Stopped)
            {
                return;
            }

            state = DiscoveryState.Stopped;
            loops = [receiveLoop, announceLoop, sweep];
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await link.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(loops).ConfigureAwait(false);
        Task lastSweepTask;
        lock (gate)
        {
            lastSweepTask = sweep;
        }

        await lastSweepTask.ConfigureAwait(false);
        stopping.Dispose();
        wake.Dispose();
    }

    internal bool RefreshInterfaces()
    {
        NetworkSnapshot snapshot;
        try
        {
            snapshot = interfaces.Read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportUnexpected(exception);
            return false;
        }

        bool changed;
        long dropped;
        lock (gate)
        {
            long now = timeProvider.GetTimestamp();
            changed = !networkKnown || !network.IsSameAs(snapshot);
            network = snapshot;
            networkKnown = true;
            lastRefresh = now;
            dropped = droppedScreenings;
            droppedScreenings = 0;
            ownNonces.Prune(now);
            seenNonces.Prune(now);
            PruneSources(now);
        }

        link.UseInterfaces(snapshot.Interfaces);
        if (dropped > 0)
        {
            logger.ScreeningLimited(dropped);
        }

        if (changed)
        {
            int count = snapshot.Interfaces.Count;
            logger.NetworkChanged(count, snapshot.HasOtherNetwork);
            observer.NetworkChanged(snapshot);
        }

        return changed;
    }

    internal async Task<int> AnnounceRoundAsync(CancellationToken cancellationToken)
    {
        bool refreshDue;
        lock (gate)
        {
            refreshDue = lastRefresh is not { } refreshed || timeProvider.GetElapsedTime(refreshed) >= options.InterfaceRefreshInterval;
        }

        if (refreshDue)
        {
            RefreshInterfaces();
        }

        NetworkSnapshot snapshot;
        int? port;
        lock (gate)
        {
            snapshot = network;
            port = announcedPort;
        }

        IReadOnlyList<SessionContact> recipients = contacts.Current;
        if (port is not { } announced || recipients.Count == 0 || snapshot.Interfaces.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<byte[]> announcements = Announcement.BuildAll(recipients, announced);
        lock (gate)
        {
            long now = timeProvider.GetTimestamp();
            foreach (byte[] announcement in announcements)
            {
                ownNonces.Add(announcement.AsSpan(0, Announcement.NonceLength), now);
            }
        }

        int sent = 0;
        foreach (LocalInterface item in snapshot.Interfaces)
        {
            foreach (byte[] announcement in announcements)
            {
                if (item.SupportsMulticast)
                {
                    sent += await SendAsync(item, options.MulticastGroup, announcement, sweep: false, cancellationToken).ConfigureAwait(false) ? 1 : 0;
                }

                if (item.HostCount > 0)
                {
                    sent += await SendAsync(item, item.Broadcast, announcement, sweep: false, cancellationToken).ConfigureAwait(false) ? 1 : 0;
                }
            }
        }

        StartSweepIfDue(announcements, snapshot.Interfaces, cancellationToken);
        return sent;
    }

    internal bool OnDatagram(ReadOnlySpan<byte> datagram, IPAddress source)
    {
        if (datagram.Length != Announcement.Length || !IsDialable(source))
        {
            return false;
        }

        ReadOnlySpan<byte> nonce = datagram[..Announcement.NonceLength];
        long now;
        lock (gate)
        {
            now = timeProvider.GetTimestamp();
            if (ownNonces.Contains(nonce, now) || seenNonces.Contains(nonce, now))
            {
                return false;
            }

            if (!TakeScreening(source, now))
            {
                droppedScreenings++;
                return false;
            }

            seenNonces.Add(nonce, now);
        }

        if (!Announcement.TryRecognize(datagram, contacts.Current, out SessionContact? sender, out int port))
        {
            return false;
        }

        lock (gate)
        {
            lastRecognized = now;
        }

        observer.Heard(sender, new IPEndPoint(source, port));
        return true;
    }

    internal bool IsSweepDue()
    {
        lock (gate)
        {
            return IsSweepDueLocked(network.Interfaces);
        }
    }

    internal async Task<int> SweepAsync(IReadOnlyList<byte[]> announcements, IReadOnlyList<LocalInterface> targets, CancellationToken cancellationToken)
    {
        int sent = 0;
        int attempts = 0;
        long started = timeProvider.GetTimestamp();
        int hosts = targets.Where(IsSweepable).Sum(static item => (int)item.HostCount - 1);
        int maxHosts = options.SweepMaxHosts;
        logger.SweepStarted(hosts, maxHosts);
        foreach (LocalInterface item in targets.Where(IsSweepable))
        {
            foreach (IPAddress host in item.OtherHosts())
            {
                foreach (byte[] announcement in announcements)
                {
                    sent += await SendAsync(item, host, announcement, sweep: true, cancellationToken).ConfigureAwait(false) ? 1 : 0;
                    attempts++;
                    if (attempts % options.SweepBatchSize == 0 && options.SweepBatchPause > TimeSpan.Zero)
                    {
                        await Task.Delay(options.SweepBatchPause, timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        long milliseconds = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        logger.SweepFinished(sent, milliseconds);
        return sent;
    }

    private static void CheckPort(int? port)
    {
        if (port is < Announcement.MinPort or > Announcement.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The announced port must be from 1 to 65535.");
        }
    }

    private static bool IsDialable(IPAddress source)
    {
        if (source.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        uint value = LocalInterface.ToValue(source);
        return value != 0 && value != uint.MaxValue && value >> 28 != 0xE;
    }

    private static TimeSpan Jittered(TimeSpan delay) => delay * (0.8 + (RandomNumberGenerator.GetInt32(0, 401) / 1000.0));

    private bool IsSweepable(LocalInterface item) => item.HostCount > 0 && item.HostCount <= options.SweepMaxHosts;

    private bool IsSweepDueLocked(IReadOnlyList<LocalInterface> targets)
    {
        if (sweeping || contacts.Current.Count == 0 || !targets.Any(IsSweepable))
        {
            return false;
        }

        long now = timeProvider.GetTimestamp();
        bool quietLongEnough = timeProvider.GetElapsedTime(lastRecognized, now) >= options.SweepAfter;
        bool restedLongEnough = lastSweep is not { } swept || timeProvider.GetElapsedTime(swept, now) >= options.SweepInterval;
        return quietLongEnough && restedLongEnough;
    }

    private void StartSweepIfDue(IReadOnlyList<byte[]> announcements, IReadOnlyList<LocalInterface> targets, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (state == DiscoveryState.Stopped || !IsSweepDueLocked(targets))
            {
                return;
            }

            sweeping = true;
            lastSweep = timeProvider.GetTimestamp();
            sweep = Task.Run(() => RunSweepAsync(announcements, targets, cancellationToken), CancellationToken.None);
        }
    }

    private async Task RunSweepAsync(IReadOnlyList<byte[]> announcements, IReadOnlyList<LocalInterface> targets, CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(announcements, targets, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportUnexpected(exception);
        }
        finally
        {
            lock (gate)
            {
                sweeping = false;
            }
        }
    }

    private async Task<bool> SendAsync(LocalInterface from, IPAddress destination, byte[] announcement, bool sweep, CancellationToken cancellationToken)
    {
        SocketError result = await link.SendAsync(from, new IPEndPoint(destination, options.Port), announcement, sweep, cancellationToken).ConfigureAwait(false);
        bool report;
        lock (gate)
        {
            bool known = lastSendErrors.TryGetValue(from.Address, out SocketError previous);
            report = result != SocketError.Success && (!known || previous != result);
            lastSendErrors[from.Address] = result;
        }

        if (report)
        {
            logger.AnnouncementNotSent(result);
        }

        return result == SocketError.Success;
    }

    private bool TakeScreening(IPAddress source, long now)
    {
        long frequency = timeProvider.TimestampFrequency;
        if (!sources.TryGetValue(source, out TokenBucket? bucket))
        {
            if (sources.Count >= options.MaxTrackedSources)
            {
                PruneSources(now);
                if (sources.Count >= options.MaxTrackedSources)
                {
                    return false;
                }
            }

            bucket = new TokenBucket(options.ScreeningsPerSecondPerSource, options.ScreeningBurstPerSource, now);
            sources.Add(source, bucket);
        }

        if (!screenings.HasToken(now, frequency) || !bucket.HasToken(now, frequency))
        {
            return false;
        }

        screenings.Take();
        bucket.Take();
        return true;
    }

    private void PruneSources(long now)
    {
        long frequency = timeProvider.TimestampFrequency;
        foreach (IPAddress idle in sources.Where(entry => entry.Value.IsFull(now, frequency)).Select(static entry => entry.Key).ToList())
        {
            sources.Remove(idle);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReceiveBufferLength];
        while (!cancellationToken.IsCancellationRequested)
        {
            DiscoveryDatagram datagram;
            try
            {
                datagram = await link.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportUnexpected(exception);
                return;
            }

            if (datagram.IsClosed)
            {
                return;
            }

            try
            {
                OnDatagram(buffer.AsSpan(0, Math.Min(datagram.Length, buffer.Length)), datagram.Source!);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportUnexpected(exception);
            }
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await AnnounceRoundAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportUnexpected(exception);
            }

            try
            {
                await WaitForNextRoundAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task WaitForNextRoundAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(Jittered(options.AnnounceInterval), timeProvider, waiting.Token);
        Task woken = wake.WaitAsync(waiting.Token);
        Task first = await Task.WhenAny(delay, woken).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(first, woken))
        {
            try
            {
                await woken.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ReportUnexpected(Exception exception)
    {
        string exceptionType = exception.GetType().Name;
        logger.DiscoveryUnexpected(exceptionType);
    }
}
