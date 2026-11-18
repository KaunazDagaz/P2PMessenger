using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace P2PMessenger.Application.Logging;

internal sealed class TraceLogger : ILogger
{
    private readonly string category;
    private readonly LogLevel minimum;

    public TraceLogger(string category, LogLevel minimum)
    {
        this.category = category;
        this.minimum = minimum;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (IsEnabled(logLevel))
        {
            Trace.WriteLine($"{logLevel} {category}[{eventId.Id}]: {formatter(state, null)}");
        }
    }
}
