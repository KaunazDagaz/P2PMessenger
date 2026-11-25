using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace P2PMessenger.UI.Services;

public sealed class GlobalErrorHandler
{
    private static readonly Action<ILogger, string, ErrorOrigin, Exception?> LogUnhandled = LoggerMessage.Define<string, ErrorOrigin>(
        LogLevel.Error,
        new EventId(1, "UnhandledException"),
        "Unhandled exception of type {ExceptionType} on {Origin}");

    private readonly ILogger logger;
    private readonly IUiThread uiThread;
    private readonly Action onError;
    private readonly ConditionalWeakTable<Exception, object> reported = [];
    private bool installed;

    public GlobalErrorHandler(ILogger logger, IUiThread uiThread, Action onError)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(onError);

        this.logger = logger;
        this.uiThread = uiThread;
        this.onError = onError;
    }

    public void Install()
    {
        if (installed)
        {
            return;
        }

        installed = true;
        Dispatcher.UIThread.UnhandledException += OnUiThreadException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
    }

    public void Uninstall()
    {
        if (!installed)
        {
            return;
        }

        installed = false;
        Dispatcher.UIThread.UnhandledException -= OnUiThreadException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainException;
    }

    public bool Handle(Exception exception, ErrorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception cause = Unwrap(exception);
        if (cause is OutOfMemoryException)
        {
            return false;
        }

        if (!reported.TryAdd(cause, reported))
        {
            return true;
        }
        LogUnhandled(logger, cause.GetType().FullName ?? cause.GetType().Name, origin, null);
        if (origin != ErrorOrigin.AppDomain)
        {
            uiThread.Post(onError);
        }

        return true;
    }

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions is [var single, ..] ? single : exception;

    private void OnUiThreadException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = Handle(e.Exception, ErrorOrigin.UiThread);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (Handle(e.Exception, ErrorOrigin.BackgroundTask))
        {
            e.SetObserved();
        }
    }

    private void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Handle(exception, ErrorOrigin.AppDomain);
        }
    }
}
