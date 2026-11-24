using Android;
using Android.Content;
using Android.Content.PM;
using P2PMessenger.Android.Scanning;
using P2PMessenger.Application.Background;

namespace P2PMessenger.Android.Background;

internal sealed class AndroidMessageNotifier : IMessageNotifier
{
    private readonly Context context;

    public AndroidMessageNotifier(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
    }

    public bool IsAllowed
    {
        get
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && context.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                return false;
            }

            return context.GetSystemService(Context.NotificationService) is NotificationManager manager && manager.AreNotificationsEnabled();
        }
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        if (IsAllowed || !OperatingSystem.IsAndroidVersionAtLeast(33) || MainActivity.Current is not { } activity)
        {
            return IsAllowed;
        }

        _ = await ActivityResults.RequestPermissionAsync(activity, Manifest.Permission.PostNotifications).WaitAsync(cancellationToken).ConfigureAwait(false);
        return IsAllowed;
    }

    public void Show(MessageNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        Notifications.ShowMessage(context, notice);
        MessengerService.Current?.Record("notification: a message notification was shown");
    }
}
