namespace P2PMessenger.Spikes.Tor;

public sealed record TorLaunchOptions(string TorExecutable, string DataDirectory)
{
    public string? LogFile { get; init; }

    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
