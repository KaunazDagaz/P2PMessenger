using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using P2PMessenger.Android.KeyStores;
using P2PMessenger.Android.Scanning;
using P2PMessenger.UI;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    private const string DataFolderName = "P2PMessenger";

    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CreateAppBuilder() =>
        AppBuilder.Configure(() => new App(new AppPlatform(
            DataFolder(),
            new AndroidKeystoreKeyProtector(new AndroidKeystoreKeys(this)),
            MainActivity.HideFromRecents,
            new AndroidInterfaceSource(),
            new AndroidQrScanner()))).UseAndroid();

    private string? DataFolder() =>
        NoBackupFilesDir?.AbsolutePath is { Length: > 0 } folder ? System.IO.Path.Combine(folder, DataFolderName) : null;
}
