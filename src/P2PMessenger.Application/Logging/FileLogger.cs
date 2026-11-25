using Microsoft.Extensions.Logging;
using P2PMessenger.Core;

namespace P2PMessenger.Application.Logging;

internal sealed class FileLogger : ILogger
{
    private readonly string category;
    private readonly LogFile file;
    private readonly LogLevel minimum;
    private readonly TimeProvider timeProvider;

    public FileLogger(string category, LogFile file, LogLevel minimum, TimeProvider timeProvider)
    {
        this.category = category;
        this.file = file;
        this.minimum = minimum;
        this.timeProvider = timeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            file.Append(LogLine.Format(timeProvider.GetUtcNow(), category, logLevel, eventId, state, exception));
        }
        catch (Exception failure) when (Failures.IsRecoverable(failure))
        {
            return;
        }
    }
}
