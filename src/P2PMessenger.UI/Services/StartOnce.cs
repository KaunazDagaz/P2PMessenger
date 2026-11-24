namespace P2PMessenger.UI.Services;

public sealed class StartOnce
{
    private readonly Func<CancellationToken, Task> start;
    private readonly Lock sync = new();
    private Task? started;

    public StartOnce(Func<CancellationToken, Task> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        this.start = start;
    }

    public bool HasStarted
    {
        get
        {
            lock (sync)
            {
                return started is not null;
            }
        }
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return started ??= start(cancellationToken);
        }
    }
}
