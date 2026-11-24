using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Application.Internet;

public sealed record TorSetup
{
    public const string SocketFileName = "onion.sock";

    public string? ExecutablePath { get; init; }

    public required string DataFolder { get; init; }

    public bool UseUnixSocket { get; init; } = true;

    public bool Enabled { get; init; } = true;

    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsInBuild => ExecutablePath is { Length: > 0 } path && Path.IsPathFullyQualified(path) && File.Exists(path);

    public string SocketPath => Path.Combine(DataFolder, SocketFileName);

    public static TorSetup For(string? executablePath, string dataFolder) => new() { ExecutablePath = executablePath, DataFolder = dataFolder };

    public bool DeleteData()
    {
        try
        {
            if (Directory.Exists(DataFolder))
            {
                Directory.Delete(DataFolder, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal TorOptions ToOptions() => new()
    {
        ExecutablePath = ExecutablePath ?? throw new InvalidOperationException("tor is not part of this build."),
        DataFolder = DataFolder,
        RestartDelay = RestartDelay,
    };
}
