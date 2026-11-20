using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P2PMessenger.Application;
using P2PMessenger.Application.Logging;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI;

public partial class App : Avalonia.Application
{
    private readonly AppPlatform platform;
    private MainView? activityView;

    public App()
        : this(AppPlatform.WithoutKeyStore)
    {
    }

    public App(AppPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        this.platform = platform;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IActivityApplicationLifetime activityLifetime:
                MainViewModel activityShell = new(CreateServices(() => activityView is null ? null : TopLevel.GetTopLevel(activityView)));
                activityLifetime.MainViewFactory = () => activityView = new MainView { DataContext = activityShell };
                _ = activityShell.StartAsync(CancellationToken.None);
                break;

            case IClassicDesktopStyleApplicationLifetime desktopLifetime:
                AppServices desktopServices = CreateServices(() => desktopLifetime.MainWindow);
                MainViewModel desktopShell = new(desktopServices);
                desktopLifetime.MainWindow = new MainWindow { DataContext = desktopShell };
                desktopLifetime.Exit += (_, _) => desktopServices.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _ = desktopShell.StartAsync(CancellationToken.None);
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ILoggerFactory CreateLoggers() =>
        BuildInfo.IsDebug ? new TraceLoggerFactory(LogLevel.Information) : NullLoggerFactory.Instance;

    private AppServices CreateServices(Func<TopLevel?> topLevel)
    {
        TopLevelServices topLevelServices = new(topLevel);
        return new AppServices(
            platform.CreateLocalData(),
            MessengerSetup.InMemory() with { Loggers = CreateLoggers() },
            new AvaloniaUiThread(),
            topLevelServices,
            topLevelServices);
    }
}
