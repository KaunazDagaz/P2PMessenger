namespace P2PMessenger.Application.Delivery;

public interface IWakeUpScheduler
{
    IDisposable Every(TimeSpan interval, Action wakeUp);
}
