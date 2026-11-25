using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Core;

namespace P2PMessenger.UI.Services;

public sealed class FailureLog
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogCommandFailed = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(2, "CommandFailed"),
        "{Page} could not finish {Operation}: {ExceptionType}");

    private readonly ILogger logger;

    public FailureLog(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    public static FailureLog None { get; } = new(NullLogger.Instance);

    public void Report(Type page, string operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(exception);

        LogCommandFailed(logger, page.Name, operation, Failures.TypeName(exception), null);
    }
}
