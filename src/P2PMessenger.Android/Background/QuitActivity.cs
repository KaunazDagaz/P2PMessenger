using Android.OS;
using Android.Runtime;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.Android.Background;

[Register("io.github.kaunazdagaz.p2pmessenger.QuitActivity")]
public sealed class QuitActivity : Activity
{
    private AlertDialog? dialog;
    private bool quitting;

    public QuitActivity()
    {
    }

    private QuitActivity(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        using AlertDialog.Builder builder = new(this, global::Android.Resource.Style.ThemeDeviceDefaultDialogAlert);
        _ = builder.SetTitle(UiText.BackgroundQuitQuestion)
            ?.SetMessage(UiText.BackgroundQuitWarning)
            ?.SetPositiveButton(UiText.BackgroundQuitConfirm, (_, _) => Quit())
            ?.SetNegativeButton(UiText.CommonCancel, (_, _) => Finish());
        dialog = builder.Create();
        if (dialog is null)
        {
            Finish();
            return;
        }

        dialog.DismissEvent += (_, _) =>
        {
            if (!quitting)
            {
                Finish();
            }
        };
        dialog.Show();
    }

    protected override void OnDestroy()
    {
        dialog?.Dispose();
        dialog = null;
        base.OnDestroy();
    }

    private void Quit()
    {
        quitting = true;
        FinishAndRemoveTask();
        if (MainApplication.BackgroundOf(this) is { } background)
        {
            background.RequestQuit();
        }
        else
        {
            Process.KillProcess(Process.MyPid());
        }
    }
}
