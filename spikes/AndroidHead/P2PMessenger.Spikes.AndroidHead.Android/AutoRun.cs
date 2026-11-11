using Android.Content;
using P2PMessenger.Spikes.AndroidHead.Probe;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal static class AutoRun
{
    public static event Action<string>? ViewCommand;

    public static void Handle(Intent? intent, SpikeServices services)
    {
        var command = intent?.GetStringExtra("run");
        if (command is null)
        {
            return;
        }

        if (command is "scan-view" or "scan-view-hide")
        {
            SpikeLog.Write($"auto: {command}");
            ViewCommand?.Invoke(command);
            return;
        }

        var kind = Enum.TryParse<ServiceKind>(intent?.GetStringExtra("kind"), out var parsed) ? parsed : ServiceKind.ConnectedDevice;
        var request = new ServiceRequest(
            kind,
            intent?.GetBooleanExtra("wakelock", false) ?? false,
            intent?.GetIntExtra("port", ProbeProtocol.DefaultPort) ?? ProbeProtocol.DefaultPort);
        SpikeLog.Write($"auto: {command} (type {request.Kind}, wake lock {request.WakeLock}, port {request.Port})");
        _ = Task.Run(async () =>
        {
            try
            {
                var result = command switch
                {
                    "info" => services.Device.Describe(),
                    "fgs-start" => services.Background.Start(request),
                    "fgs-stop" => services.Background.Halt(),
                    "notifications" => await services.Permissions.RequestNotificationsAsync().ConfigureAwait(false),
                    "camera-permission" => await services.Permissions.RequestCameraAsync().ConfigureAwait(false),
                    "battery" => await services.Permissions.RequestBatteryExemptionAsync().ConfigureAwait(false),
                    "scan-activity" => await services.Scanner.ScanInActivityAsync().ConfigureAwait(false),
                    "tor-selftest" => services.Tor.Start("selftest", request),
                    "tor-pair" => services.Tor.Start("pair", request),
                    "tor-serve" => services.Tor.Start("serve", request),
                    "tor-stop" => services.Tor.Halt(),
                    _ => $"unknown command {command}",
                };
                SpikeLog.Write($"auto: {command} -> {result}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SpikeLog.Write($"auto: {command} failed: {exception.GetType().Name}: {exception.Message}");
            }
        });
    }
}
