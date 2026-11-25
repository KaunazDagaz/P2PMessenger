using Microsoft.Extensions.Logging;

namespace P2PMessenger.Application.Logging;

public sealed class LogFileOptions
{
    public const string DefaultFileName = "p2pmessenger.log";
    public const long DefaultMaxFileBytes = 1024 * 1024;
    public const int DefaultMaxFiles = 3;

    public required string Folder { get; init; }

    public string FileName { get; init; } = DefaultFileName;

    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;

    public int MaxFiles { get; init; } = DefaultMaxFiles;

    public LogLevel Minimum { get; init; } = LogLevel.Information;

    public TimeSpan RetryAfterFailure { get; init; } = TimeSpan.FromSeconds(30);

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Folder))
        {
            return "The log folder must be named.";
        }

        if (string.IsNullOrWhiteSpace(FileName) || FileName != Path.GetFileName(FileName))
        {
            return "The log file name must be a plain file name.";
        }

        if (MaxFileBytes < 1)
        {
            return "A log file must be allowed at least one byte.";
        }

        if (MaxFiles < 1)
        {
            return "At least one log file must be kept.";
        }

        return RetryAfterFailure < TimeSpan.Zero ? "The wait after a failed write cannot be negative." : null;
    }
}
