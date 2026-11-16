using System.Globalization;
using Microsoft.Extensions.Logging;

namespace P2PMessenger.Integration.Tests.Connections;

internal sealed class LogCapture
{
    private readonly Lock gate = new();
    private readonly List<LogEntry> entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public void Add(LogEntry entry)
    {
        lock (gate)
        {
            entries.Add(entry);
        }
    }
}

internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, IReadOnlyList<string> Values, Exception? Exception);

internal sealed class CapturingLogger<T>(LogCapture capture) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        IReadOnlyList<string> values = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? [.. pairs.Select(pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)]
            : [];
        capture.Add(new LogEntry(logLevel, eventId, formatter(state, exception), values, exception));
    }
}
