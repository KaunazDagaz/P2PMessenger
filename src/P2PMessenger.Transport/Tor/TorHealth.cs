namespace P2PMessenger.Transport.Tor;

internal sealed class TorHealth
{
    private readonly TimeSpan warningGrace;
    private readonly TimeSpan bootstrapLimit;
    private readonly TimeProvider timeProvider;
    private long startedAt;
    private int progress;
    private bool bootstrapped;
    private bool warned;
    private bool clockWrong;
    private bool directoriesUnreachable;
    private bool networkDown;
    private bool circuitsDown;
    private long circuitsDownAt;
    private bool published;
    private string? serviceId;

    public TorHealth(TimeSpan warningGrace, TimeSpan bootstrapLimit, TimeProvider timeProvider)
    {
        this.warningGrace = warningGrace;
        this.bootstrapLimit = bootstrapLimit;
        this.timeProvider = timeProvider;
        startedAt = timeProvider.GetTimestamp();
    }

    public void Restart()
    {
        startedAt = timeProvider.GetTimestamp();
        progress = 0;
        bootstrapped = false;
        warned = false;
        clockWrong = false;
        directoriesUnreachable = false;
        networkDown = false;
        circuitsDown = false;
        circuitsDownAt = 0;
        published = false;
        serviceId = null;
    }

    public void ExpectService(string id) => serviceId = id;

    public void Apply(TorEvent torEvent)
    {
        ArgumentNullException.ThrowIfNull(torEvent);
        switch (torEvent.Kind)
        {
            case TorEventKind.Bootstrap:
                progress = torEvent.Progress;
                if (torEvent.Progress == 100)
                {
                    bootstrapped = true;
                    warned = false;
                    clockWrong = false;
                    directoriesUnreachable = false;
                    circuitsDown = false;
                }
                else if (torEvent.Warning)
                {
                    warned = true;
                }

                break;
            case TorEventKind.CircuitsEstablished:
                circuitsDown = false;
                clockWrong = false;
                directoriesUnreachable = false;
                break;
            case TorEventKind.CircuitsNotEstablished:
                if (!circuitsDown)
                {
                    circuitsDown = true;
                    circuitsDownAt = timeProvider.GetTimestamp();
                }

                break;
            case TorEventKind.ClockSkew:
                clockWrong = true;
                break;
            case TorEventKind.DirectoriesUnreachable:
                directoriesUnreachable = true;
                break;
            case TorEventKind.NetworkDown:
                networkDown = true;
                break;
            case TorEventKind.NetworkUp:
                networkDown = false;
                break;
            case TorEventKind.DescriptorUploaded when serviceId is not null && string.Equals(torEvent.ServiceId, serviceId, StringComparison.Ordinal):
                published = true;
                break;
            default:
                break;
        }
    }

    public TorStatus Evaluate()
    {
        long now = timeProvider.GetTimestamp();
        if (bootstrapped)
        {
            if (!circuitsDown || timeProvider.GetElapsedTime(circuitsDownAt, now) < warningGrace)
            {
                return new TorStatus(TorState.Connected, 100, TorProblem.None, published);
            }

            return new TorStatus(TorState.Unavailable, 100, clockWrong ? TorProblem.ClockWrong : TorProblem.CannotReachTor, published);
        }

        if (clockWrong)
        {
            return new TorStatus(TorState.Unavailable, progress, TorProblem.ClockWrong, published);
        }

        TimeSpan elapsed = timeProvider.GetElapsedTime(startedAt, now);
        bool trouble = warned || directoriesUnreachable || networkDown;
        if ((trouble && elapsed >= warningGrace) || elapsed >= bootstrapLimit)
        {
            return new TorStatus(TorState.Unavailable, progress, TorProblem.CannotReachTor, published);
        }

        return new TorStatus(TorState.Starting, progress, TorProblem.None, published);
    }
}
