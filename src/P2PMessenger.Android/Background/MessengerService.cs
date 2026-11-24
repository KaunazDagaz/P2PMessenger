using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Runtime;
using P2PMessenger.UI.Services;

namespace P2PMessenger.Android.Background;

[Register("io.github.kaunazdagaz.p2pmessenger.MessengerService")]
public sealed class MessengerService : Service
{
    public const string ActionRun = "io.github.kaunazdagaz.p2pmessenger.action.RUN";
    public const string ActionResume = "io.github.kaunazdagaz.p2pmessenger.action.RESUME";

    private const string MulticastTag = "p2pm:discovery";

    private static readonly Lock Sync = new();
    private static WeakReference<MessengerService>? current;

    private WifiManager.MulticastLock? multicastLock;
    private ServiceDiagnostics? diagnostics;
    private ScreenReceiver? screenReceiver;

    public MessengerService()
    {
    }

    private MessengerService(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public static MessengerService? Current
    {
        get
        {
            lock (Sync)
            {
                MessengerService? service = null;
                return current?.TryGetTarget(out service) == true ? service : null;
            }
        }
    }

    public static bool Start(Context context, string action)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            using Intent intent = new(context, typeof(MessengerService));
            _ = intent.SetAction(action);
            _ = context.StartForegroundService(intent);
            return true;
        }
        catch (Java.Lang.IllegalStateException)
        {
            return false;
        }
        catch (Java.Lang.SecurityException)
        {
            return false;
        }
    }

    public static void Stop(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using Intent intent = new(context, typeof(MessengerService));
        _ = context.StopService(intent);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (!TryStartForeground())
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        lock (Sync)
        {
            current = new WeakReference<MessengerService>(this);
        }

        AcquireMulticastLock();
        WatchScreen();
        if (BuildInfo.IsDebug && diagnostics is null)
        {
            diagnostics = ServiceDiagnostics.Open(this);
        }

        diagnostics?.Write(BackgroundRules.StartLine(intent?.Action, IsForeground(), AndroidBatteryExemption.IsBackgroundRestricted(this)));
        if (intent?.Action != ActionRun)
        {
            MainApplication.BackgroundOf(this)?.RequestResume();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        diagnostics?.Write("service: stopping");
        lock (Sync)
        {
            if (current?.TryGetTarget(out MessengerService? service) == true && ReferenceEquals(service, this))
            {
                current = null;
            }
        }

        if (screenReceiver is { } watching)
        {
            screenReceiver = null;
            UnregisterReceiver(watching);
            watching.Dispose();
        }

        if (multicastLock is { } held)
        {
            multicastLock = null;
            if (held.IsHeld)
            {
                held.Release();
            }

            held.Dispose();
        }

        diagnostics?.Dispose();
        diagnostics = null;
        base.OnDestroy();
    }

    internal void Record(string line) => diagnostics?.Write(line);

    private bool? IsForeground() =>
        OperatingSystem.IsAndroidVersionAtLeast(29) ? ForegroundServiceType != ForegroundService.TypeNone : null;

    private bool TryStartForeground()
    {
        try
        {
            using Notification notification = Notifications.BuildService(this);
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(BackgroundRules.ServiceNotificationId, notification, ForegroundService.TypeConnectedDevice);
            }
            else
            {
                StartForeground(BackgroundRules.ServiceNotificationId, notification);
            }

            return true;
        }
        catch (Java.Lang.IllegalStateException)
        {
            return false;
        }
        catch (Java.Lang.SecurityException)
        {
            return false;
        }
    }

    private void WatchScreen()
    {
        if (screenReceiver is not null)
        {
            return;
        }

        screenReceiver = new ScreenReceiver();
        using IntentFilter filter = new();
        filter.AddAction(BackgroundRules.ScreenOff);
        filter.AddAction(BackgroundRules.UserPresent);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _ = RegisterReceiver(screenReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            _ = RegisterReceiver(screenReceiver, filter);
        }
    }

    private void AcquireMulticastLock()
    {
        if (multicastLock?.IsHeld == true)
        {
            return;
        }

        if (ApplicationContext?.GetSystemService(WifiService) is not WifiManager wifi)
        {
            return;
        }

        multicastLock = wifi.CreateMulticastLock(MulticastTag);
        multicastLock?.SetReferenceCounted(false);
        multicastLock?.Acquire();
    }
}
