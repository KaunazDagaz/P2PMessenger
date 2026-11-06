using Android.Content.PM;
using Avalonia.Android;

namespace P2PMessenger.Android;

[Activity(
    Label = "P2PMessenger",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
