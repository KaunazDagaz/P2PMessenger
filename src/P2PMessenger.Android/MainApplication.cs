using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using P2PMessenger.Android.Background;
using P2PMessenger.Android.KeyStores;
using P2PMessenger.Android.Scanning;
using P2PMessenger.Android.Tor;
using P2PMessenger.Application.Background;
using P2PMessenger.UI;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    private const string DataFolderName = "P2PMessenger";
    private const string SettingsFileName = "p2pm-settings";

    private ServiceProvider? services;

    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    internal static AndroidBackground? BackgroundOf(Context context) =>
        (context.ApplicationContext as MainApplication)?.services?.GetService<AndroidBackground>();

    protected override AppBuilder CreateAppBuilder()
    {
        services ??= Compose();
        ServiceProvider composed = services;
        return AppBuilder.Configure(() => composed.GetRequiredService<App>()).UseAndroid();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services?.Dispose();
            services = null;
        }

        base.Dispose(disposing);
    }

    private ServiceProvider Compose()
    {
        ServiceCollection collection = new();
        _ = collection.AddSingleton(_ => new BackgroundState(new PreferenceFlags(this, BackgroundState.FileName)));
        _ = collection.AddSingleton(provider => new AndroidBackground(this, provider.GetRequiredService<BackgroundState>()));
        _ = collection.AddSingleton<IBatteryExemption>(_ => new AndroidBatteryExemption(this));
        _ = collection.AddSingleton<IMessageNotifier>(_ => new AndroidMessageNotifier(this));
        _ = collection.AddSingleton<ISettingsStore>(_ => new PreferenceFlags(this, SettingsFileName));
        _ = collection.AddSingleton(provider => new BackgroundPorts(
            provider.GetRequiredService<AndroidBackground>(),
            provider.GetRequiredService<IBatteryExemption>(),
            provider.GetRequiredService<IMessageNotifier>(),
            provider.GetRequiredService<ISettingsStore>(),
            () => MainActivity.IsVisible));
        _ = collection.AddSingleton(provider => new AppPlatform(
            DataFolder(),
            new AndroidKeystoreKeyProtector(new AndroidKeystoreKeys(this)),
            MainActivity.HideFromRecents,
            new AndroidInterfaceSource(),
            new AndroidQrScanner())
        {
            Background = provider.GetRequiredService<BackgroundPorts>(),
            Tor = AndroidTor.Setup(ApplicationInfo?.NativeLibraryDir, NoBackupFilesDir?.AbsolutePath),
            Settings = provider.GetRequiredService<ISettingsStore>(),
        });
        _ = collection.AddTransient(provider => new App(provider.GetRequiredService<AppPlatform>()));
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private string? DataFolder() =>
        NoBackupFilesDir?.AbsolutePath is { Length: > 0 } folder ? System.IO.Path.Combine(folder, DataFolderName) : null;
}
