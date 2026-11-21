namespace P2PMessenger.Application.Delivery;

public sealed class TimerWakeUpScheduler : IWakeUpScheduler
{
    private readonly TimeProvider timeProvider;

    public TimerWakeUpScheduler(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    public IDisposable Every(TimeSpan interval, Action wakeUp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(wakeUp);

        return timeProvider.CreateTimer(static state => ((Action)state!)(), wakeUp, interval, interval);
    }
}
