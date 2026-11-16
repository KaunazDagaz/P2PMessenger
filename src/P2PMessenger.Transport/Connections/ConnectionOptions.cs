namespace P2PMessenger.Transport.Connections;

public sealed record ConnectionOptions
{
    private static readonly TimeSpan LongestTime = TimeSpan.FromDays(1);

    public TimeSpan DialTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HintTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan SimultaneousOpenWindow { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReconnectDelayMin { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectDelayMax { get; init; } = TimeSpan.FromSeconds(10);

    public AdmissionLimits Admission { get; init; } = new();

    internal string? FindProblem()
    {
        TimeSpan[] times =
        [
            DialTimeout,
            HintTimeout,
            HandshakeTimeout,
            WriteTimeout,
            KeepAliveInterval,
            IdleTimeout,
            SimultaneousOpenWindow,
            ReconnectDelayMin,
            ReconnectDelayMax,
        ];

        if (Array.Exists(times, static time => time <= TimeSpan.Zero || time > LongestTime))
        {
            return "Every time must be positive and at most one day.";
        }

        if (IdleTimeout <= KeepAliveInterval)
        {
            return "The idle timeout must be longer than the keep-alive interval.";
        }

        if (ReconnectDelayMax < ReconnectDelayMin)
        {
            return "The longest reconnect delay must not be shorter than the shortest one.";
        }

        return Admission is null ? "The admission limits are missing." : Admission.FindProblem();
    }
}
