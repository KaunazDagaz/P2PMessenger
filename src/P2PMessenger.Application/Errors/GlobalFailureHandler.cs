using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core;

namespace P2PMessenger.Application.Errors;

public sealed class GlobalFailureHandler : IFailureSink
{
    private static readonly Action<ILogger, string, FailureSource, Exception?> LogFailure = LoggerMessage.Define<string, FailureSource>(
        LogLevel.Error,
        new EventId(1, "UnexpectedFailure"),
        "Unexpected failure of type {ExceptionType} in {Source}");

    private static readonly Action<ILogger, string, Exception?> LogSubscriberFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2, "SubscriberFailed"),
        "A subscriber of a failure report threw {ExceptionType}");

    private readonly ILogger logger;
    private readonly ConditionalWeakTable<Exception, object> reported = [];
    private readonly Lock gate = new();
    private long count;
    private bool installed;

    public GlobalFailureHandler(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    public event EventHandler<FailureReport>? FailureReported;

    public long Count => Interlocked.Read(ref count);

    public void Install()
    {
        lock (gate)
        {
            if (installed)
            {
                return;
            }

            installed = true;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
        }
    }

    public void Uninstall()
    {
        lock (gate)
        {
            if (!installed)
            {
                return;
            }

            installed = false;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainException;
        }
    }

    public void Report(FailureSource source, Exception exception) => _ = Handle(exception, source);

    public bool Handle(Exception exception, FailureSource source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception cause = Unwrap(exception);
        if (!Failures.IsRecoverable(cause))
        {
            return false;
        }

        if (!reported.TryAdd(cause, reported))
        {
            return true;
        }

        string exceptionType = Failures.TypeName(cause);
        _ = Interlocked.Increment(ref count);
        LogFailure(logger, exceptionType, source, null);
        Raise(new FailureReport(source, exceptionType));
        return true;
    }

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions is [var single, ..] ? single : exception;

    private void Raise(FailureReport report)
    {
        EventHandler<FailureReport>? handlers = FailureReported;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<FailureReport>)subscriber)(this, report);
            }
            catch (Exception exception) when (Failures.IsRecoverable(exception))
            {
                LogSubscriberFailed(logger, Failures.TypeName(exception), null);
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (Handle(e.Exception, FailureSource.BackgroundTask))
        {
            e.SetObserved();
        }
    }

    private void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _ = Handle(exception, FailureSource.AppDomain);
        }
    }
}
