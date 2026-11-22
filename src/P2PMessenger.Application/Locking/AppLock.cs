using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Application.Locking;

public sealed class AppLock : IDisposable
{
    private readonly ILocalData localData;
    private readonly TimeProvider timeProvider;
    private readonly Lock sync = new();
    private ITimer? timer;
    private DateTimeOffset? backgroundSince;
    private long backgroundTimestamp;
    private bool locked;
    private bool disposed;

    public AppLock(ILocalData localData, TimeProvider timeProvider)
        : this(localData, timeProvider, DefaultBackgroundLimit)
    {
    }

    public AppLock(ILocalData localData, TimeProvider timeProvider, TimeSpan backgroundLimit)
    {
        ArgumentNullException.ThrowIfNull(localData);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(backgroundLimit, TimeSpan.Zero);

        this.localData = localData;
        this.timeProvider = timeProvider;
        BackgroundLimit = backgroundLimit;
    }

    public event EventHandler? Changed;

    public static TimeSpan DefaultBackgroundLimit { get; } = TimeSpan.FromMinutes(5);

    public TimeSpan BackgroundLimit { get; }

    public bool IsEnabled => localData.Protection == LocalDataProtection.Passphrase;

    public bool IsLocked
    {
        get
        {
            lock (sync)
            {
                return locked;
            }
        }
    }

    public bool IsInBackground
    {
        get
        {
            lock (sync)
            {
                return backgroundSince is not null;
            }
        }
    }

    public void EnterBackground()
    {
        lock (sync)
        {
            if (disposed || backgroundSince is not null)
            {
                return;
            }

            backgroundSince = timeProvider.GetUtcNow();
            backgroundTimestamp = timeProvider.GetTimestamp();
            timer?.Dispose();
            timer = timeProvider.CreateTimer(OnBackgroundLimit, null, BackgroundLimit, Timeout.InfiniteTimeSpan);
        }
    }

    public void EnterForeground()
    {
        bool tooLong;
        lock (sync)
        {
            if (disposed || backgroundSince is not { } since)
            {
                return;
            }

            timer?.Dispose();
            timer = null;
            TimeSpan byClock = timeProvider.GetUtcNow() - since;
            TimeSpan byTimer = timeProvider.GetElapsedTime(backgroundTimestamp);
            backgroundSince = null;
            tooLong = byClock < TimeSpan.Zero || byClock >= BackgroundLimit || byTimer >= BackgroundLimit;
        }

        if (tooLong)
        {
            LockNow();
        }
    }

    public void LockNow()
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (sync)
        {
            if (disposed || locked)
            {
                return;
            }

            locked = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> UnlockAsync(string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        if (!IsLocked)
        {
            return true;
        }

        if (!await localData.CheckPassphraseAsync(passphrase, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        lock (sync)
        {
            if (!locked)
            {
                return true;
            }

            locked = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Reset()
    {
        bool wasLocked;
        lock (sync)
        {
            timer?.Dispose();
            timer = null;
            backgroundSince = null;
            wasLocked = locked;
            locked = false;
        }

        if (wasLocked)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
    }

    private void OnBackgroundLimit(object? state)
    {
        lock (sync)
        {
            if (backgroundSince is null)
            {
                return;
            }
        }

        LockNow();
    }
}
