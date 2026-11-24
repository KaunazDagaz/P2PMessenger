namespace P2PMessenger.Transport.Tor;

public sealed record TorOptions
{
    public required string ExecutablePath { get; init; }

    public required string DataFolder { get; init; }

    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WarningGrace { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BootstrapLimit { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal bool DisableNetwork { get; init; }

    public string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !Path.IsPathFullyQualified(ExecutablePath))
        {
            return "The tor executable needs a full path.";
        }

        if (string.IsNullOrWhiteSpace(DataFolder) || !Path.IsPathFullyQualified(DataFolder))
        {
            return "The tor data folder needs a full path.";
        }

        TimeSpan[] durations = [StartTimeout, CommandTimeout, WarningGrace, BootstrapLimit, RestartDelay, ShutdownTimeout];
        return durations.Any(static duration => duration <= TimeSpan.Zero) ? "Every tor time must be positive." : null;
    }
}
