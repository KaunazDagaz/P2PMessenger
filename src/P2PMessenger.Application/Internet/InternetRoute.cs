using System.Net;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Connections;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Internet;

public sealed class InternetRoute : IAsyncDisposable
{
    private readonly ConnectionService connections;
    private readonly TorSetup? setup;
    private readonly KeySet? keys;
    private readonly ILoggerFactory? loggers;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim switching = new(1, 1);
    private readonly Lock gate = new();
    private OnionListener? listener;
    private TorRuntime? runtime;
    private NetworkWatch? watch;
    private bool enabled;
    private InternetRouteStatus status;
    private int disposed;

    private InternetRoute(ConnectionService connections, TorSetup? setup, KeySet? keys, ILoggerFactory? loggers, TimeProvider timeProvider)
    {
        this.connections = connections;
        this.setup = setup is { IsInBuild: true } ? setup : null;
        this.keys = keys;
        this.loggers = loggers;
        this.timeProvider = timeProvider;
        status = Compute();
        if (this.setup is not null)
        {
            connections.Changed += OnConnectionsChanged;
        }
    }

    public event EventHandler? Changed;

    public InternetRouteStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }

    public bool IsInBuild => setup is not null;

    public bool IsOn
    {
        get
        {
            lock (gate)
            {
                return enabled;
            }
        }
    }

    public OnionTargetKind? TargetKind
    {
        get
        {
            lock (gate)
            {
                return listener?.Kind;
            }
        }
    }

    public async Task SetEnabledAsync(bool on, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (setup is null)
        {
            return;
        }

        await switching.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (on)
            {
                if (!IsOn)
                {
                    await StartTorAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await StopTorAsync().ConfigureAwait(false);
                setup.DeleteData();
            }
        }
        finally
        {
            switching.Release();
        }

        Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        connections.Changed -= OnConnectionsChanged;
        await switching.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopTorAsync().ConfigureAwait(false);
        }
        finally
        {
            switching.Release();
        }

        switching.Dispose();
    }

    internal static InternetRoute Off(ConnectionService connections) => new(connections, null, null, null, TimeProvider.System);

    internal static async Task<InternetRoute> StartAsync(TorSetup? setup, KeySet keys, ConnectionService connections, ILoggerFactory loggers, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        InternetRoute route = new(connections, setup, keys, loggers, timeProvider);
        if (route.setup is { Enabled: true })
        {
            await route.StartTorAsync().ConfigureAwait(false);
            route.Refresh();
        }

        return route;
    }

    private async Task StartTorAsync()
    {
        TorSetup started = setup!;
        OnionListener opened = connections.StartOnionListener(started.UseUnixSocket ? started.SocketPath : null);
        TorRuntime launched;
        try
        {
            launched = TorRuntime.Start(started.ToOptions(), keys!, opened.Target, loggers!.CreateLogger<TorRuntime>(), timeProvider);
        }
        catch
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        launched.StatusChanged += OnTorChanged;
        NetworkWatch watching = new(started.NetworkSettleDelay, timeProvider, launched.NetworkChanged);
        lock (gate)
        {
            listener = opened;
            runtime = launched;
            watch = watching;
            enabled = true;
        }

        watching.Observe(connections.Network);
    }

    private async Task StopTorAsync()
    {
        TorRuntime? stopping;
        OnionListener? closing;
        NetworkWatch? watching;
        lock (gate)
        {
            stopping = runtime;
            closing = listener;
            watching = watch;
            runtime = null;
            listener = null;
            watch = null;
            enabled = false;
        }

        watching?.Dispose();

        connections.UseTorProxy(null);

        if (stopping is not null)
        {
            stopping.StatusChanged -= OnTorChanged;
            await stopping.DisposeAsync().ConfigureAwait(false);
        }

        if (closing is not null)
        {
            await closing.DisposeAsync().ConfigureAwait(false);
        }
    }

    private InternetRouteStatus Compute()
    {
        if (setup is null)
        {
            return InternetRouteStatus.NotInBuild;
        }

        if (!enabled || runtime is not { } running)
        {
            return InternetRouteStatus.Off;
        }

        return InternetRouteStatus.From(running.Status, connections.Network is not { HasAnyNetwork: false });
    }

    private void OnTorChanged(object? sender, EventArgs e) => Refresh();

    private void OnConnectionsChanged(object? sender, ConnectionChangedEventArgs e)
    {
        NetworkWatch? watching;
        lock (gate)
        {
            watching = watch;
        }

        watching?.Observe(connections.Network);
        Refresh();
    }

    private void Refresh()
    {
        bool changed;
        IPEndPoint? proxy;
        lock (gate)
        {
            InternetRouteStatus next = Compute();
            changed = next != status;
            status = next;
            proxy = next.IsAvailable ? runtime?.SocksEndpoint : null;
        }

        if (setup is not null && Volatile.Read(ref disposed) == 0)
        {
            connections.UseTorProxy(proxy);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
