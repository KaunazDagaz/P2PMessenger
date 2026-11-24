using P2PMessenger.Application.Background;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Services;

public sealed class BackgroundCoordinator : IDisposable
{
    private readonly AppServices services;
    private readonly BackgroundPorts ports;
    private readonly Func<CancellationToken, Task> startApp;
    private readonly Lock sync = new();
    private MessageAlerts? alerts;
    private bool disposed;

    public BackgroundCoordinator(AppServices services, Func<CancellationToken, Task> startApp)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(startApp);

        this.services = services;
        ports = services.Background ?? throw new ArgumentException("The services have no background ports.", nameof(services));
        this.startApp = startApp;
        services.MessengerStarted += OnMessengerStarted;
        services.MessengerStopped += OnMessengerStopped;
        ports.Service.ResumeRequested += OnResumeRequested;
        ports.Service.QuitRequested += OnQuitRequested;
    }

    public Task? Resuming { get; private set; }

    public Task? Quitting { get; private set; }

    public void EnsureRunning()
    {
        if (services.Messenger is not null)
        {
            ports.Service.StartRunning();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            alerts?.Dispose();
            alerts = null;
        }

        services.MessengerStarted -= OnMessengerStarted;
        services.MessengerStopped -= OnMessengerStopped;
        ports.Service.ResumeRequested -= OnResumeRequested;
        ports.Service.QuitRequested -= OnQuitRequested;
    }

    private void OnMessengerStarted(object? sender, EventArgs e)
    {
        if (services.Messenger is not { } messenger)
        {
            return;
        }

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            alerts?.Dispose();
            alerts = new MessageAlerts(messenger, ports.Notifier, ports.Settings, ports.IsAppVisible);
        }

        ports.Service.StartRunning();
    }

    private void OnMessengerStopped(object? sender, EventArgs e)
    {
        lock (sync)
        {
            alerts?.Dispose();
            alerts = null;
        }

        ports.Service.StopRunning();
        ports.Settings.Reset();
    }

    private void OnResumeRequested(object? sender, EventArgs e) => Resuming = ResumeAsync();

    private void OnQuitRequested(object? sender, EventArgs e)
    {
        lock (sync)
        {
            alerts?.Dispose();
            alerts = null;
        }

        Quitting = services.QuitAsync();
    }

    private async Task ResumeAsync()
    {
        try
        {
            await startApp(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ports.Service.StopRunning();
            return;
        }

        if (services.Messenger is not null)
        {
            return;
        }

        if (services.OpenStatus == LocalDataStatus.PassphraseNeeded)
        {
            ports.Service.ShowUnlockNeeded();
        }
        else
        {
            ports.Service.StopRunning();
        }
    }
}
