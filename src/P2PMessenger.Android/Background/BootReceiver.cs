using Android.Content;
using Android.Runtime;

namespace P2PMessenger.Android.Background;

[Register("io.github.kaunazdagaz.p2pmessenger.BootReceiver")]
public sealed class BootReceiver : BroadcastReceiver
{
    public BootReceiver()
    {
    }

    private BootReceiver(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || MainApplication.BackgroundOf(context) is not { } background)
        {
            return;
        }

        if (BackgroundRules.ShouldResume(intent?.Action, background.State.IsWanted))
        {
            _ = MessengerService.Start(context, MessengerService.ActionResume);
        }
    }
}
