using Android.Content;
using Android.Runtime;

namespace P2PMessenger.Android.Background;

internal sealed class ScreenReceiver : BroadcastReceiver
{
    public ScreenReceiver()
    {
    }

    private ScreenReceiver(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        switch (BackgroundRules.NoticeFor(intent?.Action, Notifications.ShownIds(context)))
        {
            case LockScreenNotice.Show:
                Notifications.ShowLockScreenNotice(context);
                break;
            case LockScreenNotice.Remove:
                Notifications.CancelLockScreenNotice(context);
                break;
        }
    }
}
