using Microsoft.Extensions.Logging;

namespace P2PMessenger.Application.Logging;

public sealed class FileLoggerFactory : ILoggerFactory
{
    private readonly LogFile file;
    private readonly LogLevel minimum;
    private readonly TimeProvider timeProvider;

    public FileLoggerFactory(LogFileOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (options.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        file = new LogFile(options, timeProvider);
        minimum = options.Minimum;
        this.timeProvider = timeProvider;
    }

    public string CurrentFile => file.PathOf(0);

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName ?? string.Empty, file, minimum, timeProvider);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("This factory writes only to its log file.");

    public void Dispose() => file.Dispose();
}
