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
    private static int started;

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

    public static bool IsVisible
    {
        get
        {
            lock (Sync)
            {
                return started > 0;
            }
        }
    }

    public static void FinishAll()
    {
        MainActivity? activity = Current;
        activity?.RunOnUiThread(activity.FinishAndRemoveTask);
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

    protected override void OnStart()
    {
        base.OnStart();
        lock (Sync)
        {
            started++;
        }
    }

    protected override void OnStop()
    {
        lock (Sync)
        {
            started = Math.Max(0, started - 1);
        }

        base.OnStop();
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
