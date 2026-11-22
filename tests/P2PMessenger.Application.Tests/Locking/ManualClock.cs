namespace P2PMessenger.Application.Tests.Locking;

internal sealed class ManualClock : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return now;
        }
    }

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return timestamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by) => Move(by, by);

    public void MoveClockOnly(TimeSpan by)
    {
        lock (gate)
        {
            now += by;
        }
    }

    public int ActiveTimers()
    {
        lock (gate)
        {
            return timers.Count(static timer => timer.Due is not null);
        }
    }

    private void Move(TimeSpan clock, TimeSpan steady)
    {
        List<ManualTimer> due;
        lock (gate)
        {
            now += clock;
            timestamp += steady.Ticks;
            due = [.. timers.Where(timer => timer.Due is { } at && at <= timestamp)];
            foreach (ManualTimer timer in due)
            {
                timer.Due = null;
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public long? Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.timestamp + dueTime.Ticks;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock.gate)
            {
                Due = null;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
