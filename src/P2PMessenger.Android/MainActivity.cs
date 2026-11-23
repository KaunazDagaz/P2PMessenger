using Android.Content.PM;
using Avalonia.Android;
using P2PMessenger.Android.Scanning;

namespace P2PMessenger.Android;

[Activity(
    Label = "P2PMessenger",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private static readonly Lock Sync = new();
    private static WeakReference<MainActivity>? resumed;
    private static WeakReference<MainActivity>? created;
    private static bool hiddenFromRecents;

    public static MainActivity? Current
    {
        get
        {
            lock (Sync)
            {
                MainActivity? activity = null;
                return resumed?.TryGetTarget(out activity) == true || created?.TryGetTarget(out activity) == true ? activity : null;
            }
        }
    }

    public static void HideFromRecents(bool hide)
    {
        MainActivity? activity = null;
        lock (Sync)
        {
            hiddenFromRecents = hide;
            resumed?.TryGetTarget(out activity);
        }

        activity?.RunOnUiThread(activity.ApplyRecents);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        lock (Sync)
        {
            created = new WeakReference<MainActivity>(this);
        }

        ActivityResult = ActivityResults.OnActivityResult;
        RequestPermissionsResult = ActivityResults.OnPermissionsResult;
    }

    protected override void OnResume()
    {
        base.OnResume();
        lock (Sync)
        {
            resumed = new WeakReference<MainActivity>(this);
        }

        ApplyRecents();
    }

    private void ApplyRecents()
    {
        bool hide;
        lock (Sync)
        {
            hide = hiddenFromRecents;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            SetRecentsScreenshotEnabled(!hide);
        }
    }
}
