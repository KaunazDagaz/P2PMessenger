using Avalonia;
using Avalonia.Headless;
using P2PMessenger.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace P2PMessenger.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
