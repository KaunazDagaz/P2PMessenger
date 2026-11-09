using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using IBinder = Android.OS.IBinder;

namespace P2PMessenger.Spikes.Tor.Android;

[Register("io.github.kaunazdagaz.torspike.KeepAliveService")]
public sealed class KeepAliveService : Service
{
    private const int NotificationId = 1;
    private const string ChannelId = "tor-spike";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        using (var channel = new NotificationChannel(ChannelId, "Tor spike", NotificationImportance.Low))
        {
            manager?.CreateNotificationChannel(channel);
        }

        using var builder = new Notification.Builder(this, ChannelId);
        builder.SetContentTitle("Tor spike");
        builder.SetContentText("A tor test is running");
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetOngoing(true);
        var notification = builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.NotSticky;
    }
}
