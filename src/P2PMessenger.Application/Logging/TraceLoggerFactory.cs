using Microsoft.Extensions.Logging;

namespace P2PMessenger.Application.Logging;

public sealed class TraceLoggerFactory : ILoggerFactory
{
    private readonly LogLevel minimum;

    public TraceLoggerFactory(LogLevel minimum)
    {
        this.minimum = minimum;
    }

    public ILogger CreateLogger(string categoryName) => new TraceLogger(categoryName, minimum);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("This factory writes only to the trace output.");

    public void Dispose()
    {
    }
}
