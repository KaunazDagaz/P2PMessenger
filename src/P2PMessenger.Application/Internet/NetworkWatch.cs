using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Internet;

internal sealed class NetworkWatch : IDisposable
{
    private readonly TimeSpan settle;
    private readonly Action<bool> changed;
    private readonly ITimer timer;
    private readonly Lock gate = new();
    private NetworkSnapshot? last;
    private bool disposed;

    public NetworkWatch(TimeSpan settle, TimeProvider timeProvider, Action<bool> changed)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(changed);

        this.settle = settle;
        this.changed = changed;
        timer = timeProvider.CreateTimer(static state => ((NetworkWatch)state!).Settled(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Observe(NetworkSnapshot? current)
    {
        if (current is null)
        {
            return;
        }

        bool first;
        lock (gate)
        {
            if (disposed || (last is not null && last.IsSameAs(current)))
            {
                return;
            }

            first = last is null;
            last = current;
            if (!first)
            {
                timer.Change(settle, Timeout.InfiniteTimeSpan);
            }
        }

        if (first && !current.HasAnyNetwork)
        {
            changed(false);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
        }

        timer.Dispose();
    }

    private void Settled()
    {
        bool hasAnyNetwork;
        lock (gate)
        {
            if (disposed || last is null)
            {
                return;
            }

            hasAnyNetwork = last.HasAnyNetwork;
        }

        changed(hasAnyNetwork);
    }
}
