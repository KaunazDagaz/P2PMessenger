using Android.Content;
using Android.OS;
using Android.Runtime;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal static class DeviceState
{
    public const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

    public static string Basic(Context context)
    {
        var power = (PowerManager?)context.GetSystemService(Context.PowerService);
        var screen = power?.IsInteractive == true ? "on" : "off";
        var idle = "none";
        if (power is not null && OperatingSystem.IsAndroidVersionAtLeast(23) && power.IsDeviceIdleMode)
        {
            idle = "deep";
        }
        else if (power is not null && OperatingSystem.IsAndroidVersionAtLeast(33) && power.IsDeviceLightIdleMode)
        {
            idle = "light";
        }

        var saver = power?.IsPowerSaveMode == true ? "on" : "off";
        return $"screen={screen} idle={idle} saver={saver} charging={(IsCharging(context) ? "yes" : "no")} exempt={(IsExempt(context) ? "yes" : "no")}";
    }

    public static string Describe(Context context, ServiceKind kind, bool wakeLockHeld) =>
        $"{Basic(context)} fgs={kind} wakelock={(wakeLockHeld ? "held" : "none")}";

    public static bool IsExempt(Context context)
    {
        var power = (PowerManager?)context.GetSystemService(Context.PowerService);
        return power is not null
            && OperatingSystem.IsAndroidVersionAtLeast(23)
            && context.PackageName is { } package
            && power.IsIgnoringBatteryOptimizations(package);
    }

    public static bool IsCharging(Context context)
    {
        using var filter = new IntentFilter(Intent.ActionBatteryChanged);
        using var sticky = context.RegisterReceiver(null, filter);
        return sticky is not null && sticky.GetIntExtra(BatteryManager.ExtraPlugged, 0) != 0;
    }

    public static string Memory()
    {
        var info = new ActivityManager.MemoryInfo();
        var manager = (ActivityManager?)Application.Context.GetSystemService(Context.ActivityService);
        manager?.GetMemoryInfo(info);
        return $"available {info.AvailMem / (1024 * 1024)} MB of {info.TotalMem / (1024 * 1024)} MB";
    }

    public static string Addresses()
    {
        var found = new List<string>();
        try
        {
            var interfaces = Java.Net.NetworkInterface.NetworkInterfaces;
            while (interfaces is not null && interfaces.HasMoreElements)
            {
                if (interfaces.NextElement() is not Java.Lang.Object element)
                {
                    continue;
                }

                using var item = element.JavaCast<Java.Net.NetworkInterface>();
                if (item is null || !item.IsUp || item.InterfaceAddresses is null)
                {
                    continue;
                }

                foreach (var address in item.InterfaceAddresses)
                {
                    if (address?.Address is Java.Net.Inet4Address inet)
                    {
                        found.Add($"{item.Name} {inet.HostAddress}/{address.NetworkPrefixLength}");
                    }
                }
            }
        }
        catch (Java.Net.SocketException exception)
        {
            return $"unknown ({exception.Message})";
        }

        return found.Count == 0 ? "none" : string.Join(", ", found);
    }
}
