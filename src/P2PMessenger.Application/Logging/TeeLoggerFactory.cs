using Microsoft.Extensions.Logging;

namespace P2PMessenger.Application.Logging;

public sealed class TeeLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory[] factories;

    public TeeLoggerFactory(params ILoggerFactory[] factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        if (factories.Length == 0 || factories.Any(static factory => factory is null))
        {
            throw new ArgumentException("At least one logger factory is needed, and none may be null.", nameof(factories));
        }

        this.factories = [.. factories];
    }

    public ILogger CreateLogger(string categoryName) => new TeeLogger([.. factories.Select(factory => factory.CreateLogger(categoryName))]);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("This factory forwards to the factories it was given.");

    public void Dispose()
    {
        foreach (ILoggerFactory factory in factories)
        {
            factory.Dispose();
        }
    }

    private sealed class TeeLogger : ILogger
    {
        private readonly ILogger[] loggers;

        public TeeLogger(ILogger[] loggers)
        {
            this.loggers = loggers;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => loggers.Any(logger => logger.IsEnabled(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (ILogger logger in loggers)
            {
                if (logger.IsEnabled(logLevel))
                {
                    logger.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }
    }
}
