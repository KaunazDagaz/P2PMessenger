using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using P2PMessenger.Application.Errors;
using P2PMessenger.Core;

namespace P2PMessenger.UI.Services;

public sealed class GlobalErrorHandler
{
    private readonly GlobalFailureHandler failures;
    private readonly IUiThread uiThread;
    private readonly Action onError;
    private bool installed;

    public GlobalErrorHandler(ILogger logger, IUiThread uiThread, Action onError)
        : this(new GlobalFailureHandler(logger), uiThread, onError)
    {
    }

    public GlobalErrorHandler(GlobalFailureHandler failures, IUiThread uiThread, Action onError)
    {
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(onError);

        this.failures = failures;
        this.uiThread = uiThread;
        this.onError = onError;
        failures.FailureReported += OnFailureReported;
    }

    public void Install()
    {
        if (installed)
        {
            return;
        }

        installed = true;
        failures.Install();
        Dispatcher.UIThread.UnhandledException += OnUiThreadException;
    }

    public void Uninstall()
    {
        if (!installed)
        {
            return;
        }

        installed = false;
        failures.Uninstall();
        Dispatcher.UIThread.UnhandledException -= OnUiThreadException;
    }

    public bool Handle(Exception exception, FailureSource source) => failures.Handle(exception, source);

    private void OnFailureReported(object? sender, FailureReport report)
    {
        if (report.Source is FailureSource.UiThread or FailureSource.BackgroundTask)
        {
            uiThread.Post(onError);
        }
    }

    private void OnUiThreadException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = Handle(e.Exception, FailureSource.UiThread);
}
