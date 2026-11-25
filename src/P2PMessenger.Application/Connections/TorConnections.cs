using System.Net;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Connections;

internal sealed class TorConnections : IAsyncDisposable
{
    private readonly ConnectionManager manager;
    private readonly ConnectionOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger dialerLogger;
    private readonly Action<Ed25519PublicKey> changed;
    private readonly Lock gate = new();
    private readonly Dictionary<Ed25519PublicKey, Target> targets = [];
    private readonly Dictionary<Ed25519PublicKey, Keeper> keepers = [];
    private readonly HashSet<Keeper> running = [];
    private readonly List<Keeper> finished = [];
    private IPEndPoint? proxy;
    private bool disposed;

    public TorConnections(ConnectionManager manager, ConnectionOptions options, TimeProvider timeProvider, ILogger dialerLogger, Action<Ed25519PublicKey> changed)
    {
        this.manager = manager;
        this.options = options;
        this.timeProvider = timeProvider;
        this.dialerLogger = dialerLogger;
        this.changed = changed;
    }

    public IPEndPoint? Proxy
    {
        get
        {
            lock (gate)
            {
                return proxy;
            }
        }
    }

    public static TorDialStatus StatusOf(SocksFailure failure) => failure switch
    {
        SocksFailure.None => TorDialStatus.None,
        SocksFailure.OnionDescriptorNotFound => TorDialStatus.NotFound,
        SocksFailure.OnionIntroductionFailed
            or SocksFailure.OnionIntroductionTimedOut
            or SocksFailure.OnionRendezvousFailed
            or SocksFailure.HostUnreachable
            or SocksFailure.ConnectionRefused
            or SocksFailure.TtlExpired => TorDialStatus.NoAnswer,
        _ => TorDialStatus.Failed,
    };

    public void Know(SessionContact contact, OnionAddress address)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(address);

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            Target target = new(contact, address);
            targets[contact.IdentityKey] = target;
            if (keepers.Remove(contact.IdentityKey, out Keeper? previous))
            {
                previous.Cancel();
            }

            if (proxy is { } current)
            {
                StartLocked(target, current);
            }
        }
    }

    public void UseProxy(IPEndPoint? next)
    {
        Ed25519PublicKey[] affected;
        lock (gate)
        {
            if (disposed || Equals(proxy, next))
            {
                return;
            }

            proxy = next;
            Keeper[] stopping = [.. keepers.Values];
            keepers.Clear();
            foreach (Keeper keeper in stopping)
            {
                keeper.Cancel();
            }

            affected = [.. targets.Keys];
            if (next is not null)
            {
                foreach (Target target in targets.Values)
                {
                    StartLocked(target, next);
                }
            }
        }

        foreach (Ed25519PublicKey key in affected)
        {
            changed(key);
        }
    }

    public TorDialStatus StatusOf(Ed25519PublicKey contactKey)
    {
        ArgumentNullException.ThrowIfNull(contactKey);

        lock (gate)
        {
            return keepers.TryGetValue(contactKey, out Keeper? keeper) ? keeper.Status : TorDialStatus.None;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] loops;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            proxy = null;
            Keeper[] stopping = [.. running];
            loops = [.. stopping.Select(static keeper => keeper.Loop)];
            keepers.Clear();
            foreach (Keeper keeper in stopping)
            {
                keeper.Cancel();
            }

            targets.Clear();
        }

        await Task.WhenAll(loops).ConfigureAwait(false);
        lock (gate)
        {
            DisposeFinishedLocked();
        }
    }

    private static TorDialStatus? StatusOf(ConnectResult result, OnionDialer dialer) => result.Failure switch
    {
        ConnectFailure.None => TorDialStatus.None,
        ConnectFailure.ShutDown => null,
        ConnectFailure.TimedOut => TorDialStatus.TimedOut,
        ConnectFailure.Refused => TorDialStatus.Refused,
        _ => StatusOf(dialer.LastFailure) is var status and not TorDialStatus.None ? status : TorDialStatus.Failed,
    };

    private void StartLocked(Target target, IPEndPoint current)
    {
        DisposeFinishedLocked();
        Keeper keeper = new(target.Contact.IdentityKey);
        keepers[keeper.Key] = keeper;
        running.Add(keeper);
        keeper.Loop = Task.Run(() => KeepAsync(keeper, target, current), CancellationToken.None);
    }

    private async Task KeepAsync(Keeper keeper, Target target, IPEndPoint current)
    {
        CancellationToken stop = keeper.Token;
        Ed25519PublicKey key = target.Contact.IdentityKey;
        TimeSpan delay = options.TorRetryDelayMin;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (manager.TryGetConnection(key, out ContactConnection? connection) && connection.Route == ConnectionRoute.Tor)
                {
                    SetStatus(keeper, key, TorDialStatus.None);
                    await connection.Completion.WaitAsync(stop).ConfigureAwait(false);
                    delay = options.TorRetryDelayMin;
                    await Task.Delay(Delays.Jittered(options.ReconnectDelayMin), timeProvider, stop).ConfigureAwait(false);
                    continue;
                }

                SetStatus(keeper, key, TorDialStatus.Connecting);
                TorDialStatus? outcome = await AttemptAsync(target, current, stop).ConfigureAwait(false);
                if (outcome is not null and not TorDialStatus.None)
                {
                    outcome = await AttemptAsync(target, current, stop).ConfigureAwait(false);
                }

                if (outcome is null)
                {
                    return;
                }

                if (outcome == TorDialStatus.None)
                {
                    delay = options.TorRetryDelayMin;
                    continue;
                }

                SetStatus(keeper, key, outcome.Value);
                await Task.Delay(Delays.Jittered(delay), timeProvider, stop).ConfigureAwait(false);
                delay = Delays.Doubled(delay, options.TorRetryDelayMax);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            Finish(keeper);
        }
    }

    private async Task<TorDialStatus?> AttemptAsync(Target target, IPEndPoint current, CancellationToken stop)
    {
        OnionDialer dialer = new(current, target.Address, dialerLogger);
        ConnectResult result = await manager.ConnectAsync(target.Contact, dialer, stop).ConfigureAwait(false);
        return result.IsConnected ? TorDialStatus.None : StatusOf(result, dialer);
    }

    private void SetStatus(Keeper keeper, Ed25519PublicKey key, TorDialStatus status)
    {
        lock (gate)
        {
            if (keeper.Status == status)
            {
                return;
            }

            keeper.Status = status;
            if (!keepers.TryGetValue(key, out Keeper? current) || !ReferenceEquals(current, keeper))
            {
                return;
            }
        }

        changed(key);
    }

    private void Finish(Keeper keeper)
    {
        lock (gate)
        {
            running.Remove(keeper);
            finished.Add(keeper);
            if (keepers.TryGetValue(keeper.Key, out Keeper? current) && ReferenceEquals(current, keeper))
            {
                keepers.Remove(keeper.Key);
            }
        }
    }

    private void DisposeFinishedLocked()
    {
        foreach (Keeper keeper in finished)
        {
            keeper.Dispose();
        }

        finished.Clear();
    }

    private sealed record Target(SessionContact Contact, OnionAddress Address);

    private sealed class Keeper : IDisposable
    {
        private readonly CancellationTokenSource stop = new();

        public Keeper(Ed25519PublicKey key)
        {
            Key = key;
            Token = stop.Token;
        }

        public Ed25519PublicKey Key { get; }

        public CancellationToken Token { get; }

        public Task Loop { get; set; } = Task.CompletedTask;

        public TorDialStatus Status { get; set; }

        public void Cancel() => stop.Cancel();

        public void Dispose() => stop.Dispose();
    }
}
