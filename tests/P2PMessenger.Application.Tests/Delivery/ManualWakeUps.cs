using P2PMessenger.Application.Delivery;

namespace P2PMessenger.Application.Tests.Delivery;

internal sealed class ManualWakeUps : IWakeUpScheduler
{
    private readonly Lock gate = new();
    private readonly List<Registration> registrations = [];

    public TimeSpan? Interval { get; private set; }

    public int Active
    {
        get
        {
            lock (gate)
            {
                return registrations.Count(static registration => !registration.IsDisposed);
            }
        }
    }

    public IDisposable Every(TimeSpan interval, Action wakeUp)
    {
        Registration registration = new(wakeUp);
        lock (gate)
        {
            Interval = interval;
            registrations.Add(registration);
        }

        return registration;
    }

    public void Fire()
    {
        Registration[] active;
        lock (gate)
        {
            active = [.. registrations.Where(static registration => !registration.IsDisposed)];
        }

        foreach (var registration in active)
        {
            registration.WakeUp();
        }
    }

    private sealed class Registration : IDisposable
    {
        private int disposed;

        public Registration(Action wakeUp)
        {
            WakeUp = wakeUp;
        }

        public Action WakeUp { get; }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    }
}
