using Avalonia;
using P2PMessenger.UI;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp(DesktopPlatform.FromArguments(args)).StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(AppPlatform.WithoutKeyStore);

    private static AppBuilder BuildAvaloniaApp(AppPlatform platform) =>
        AppBuilder.Configure(() => new App(platform))
            .UsePlatformDetect()
            .LogToTrace();
}
