using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class AndroidDeviceInfo(Context context) : IDeviceInfo
{
    public string Describe()
    {
        var culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine(culture, $"Device: {Build.Manufacturer} {Build.Model}, Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), {RuntimeInformation.ProcessArchitecture}, page size {System.Environment.SystemPageSize} bytes");
        builder.AppendLine(culture, $"App: {context.PackageName}, target API {context.ApplicationInfo?.TargetSdkVersion}, Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version}, {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine(culture, $"Memory: {DeviceState.Memory()}");
        builder.AppendLine(culture, $"State: {DeviceState.Basic(context)}");
        var notifications = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? (Granted(DeviceState.PostNotificationsPermission) ? "granted" : "not granted")
            : "not needed before Android 13";
        builder.AppendLine(culture, $"Notifications enabled for the app: {NotificationsEnabled()}; notification permission {notifications}; camera permission {(Granted("android.permission.CAMERA") ? "granted" : "not granted")}");
        builder.AppendLine(culture, $"Foreground service running: {SpikeService.IsRunning}");

        var folder = context.ApplicationInfo?.NativeLibraryDir;
        builder.AppendLine(culture, $"Native library folder: {folder ?? "none"}");
        if (folder is not null && Directory.Exists(folder))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder).Where(path => !Path.GetFileName(path).StartsWith("lib_", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                {
                    var mode = Convert.ToString((int)File.GetUnixFileMode(file), 8);
                    builder.AppendLine(culture, $"  {Path.GetFileName(file)}, {new FileInfo(file).Length} bytes, mode {mode}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                builder.AppendLine(culture, $"  The folder cannot be listed: {exception.Message}");
            }
        }

        builder.AppendLine(culture, $"Addresses: {DeviceState.Addresses()}");
        builder.Append(ActiveNetwork());
        return builder.ToString();
    }

    public void CopyToClipboard(string text)
    {
        if (context.GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("Android head spike log", text);
        }
    }

    private bool Granted(string permission) => context.CheckSelfPermission(permission) == Permission.Granted;

    private bool NotificationsEnabled() =>
        context.GetSystemService(Context.NotificationService) is NotificationManager manager && manager.AreNotificationsEnabled();

    private string ActiveNetwork()
    {
        if (context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager manager)
        {
            return "Active network: no ConnectivityManager";
        }

        var network = manager.ActiveNetwork;
        if (network is null)
        {
            return "Active network: none";
        }

        var capabilities = manager.GetNetworkCapabilities(network);
        var transports = new List<string>();
        foreach (var transport in new[] { TransportType.Wifi, TransportType.Cellular, TransportType.Ethernet, TransportType.Vpn })
        {
            if (capabilities?.HasTransport(transport) == true)
            {
                transports.Add(transport.ToString());
            }
        }

        return $"Active network: {string.Join("+", transports)}, internet {(capabilities?.HasCapability(NetCapability.Internet) == true ? "yes" : "no")}, validated {(capabilities?.HasCapability(NetCapability.Validated) == true ? "yes" : "no")}";
    }
}
