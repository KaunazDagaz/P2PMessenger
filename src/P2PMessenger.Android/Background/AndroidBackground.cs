using Android.Content;
using Android.OS;
using P2PMessenger.Application.Background;

namespace P2PMessenger.Android.Background;

public sealed class AndroidBackground : IBackgroundService
{
    private readonly Context context;

    public AndroidBackground(Context context, BackgroundState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        this.context = context;
        State = state;
    }

    public event EventHandler? ResumeRequested;

    public event EventHandler? QuitRequested;

    public BackgroundState State { get; }

    public void StartRunning()
    {
        State.IsWanted = true;
        Notifications.CancelUnlockNeeded(context);
        MessengerService.Start(context, MessengerService.ActionRun);
    }

    public void StopRunning()
    {
        State.IsWanted = false;
        MessengerService.Stop(context);
    }

    public void ShowUnlockNeeded()
    {
        Notifications.ShowUnlockNeeded(context);
        MessengerService.Stop(context);
    }

    public void EndProcess()
    {
        MainActivity.FinishAll();
        _ = Task.Run(async () =>
        {
            await Task.Delay(BackgroundRules.EndProcessDelay).ConfigureAwait(false);
            Process.KillProcess(Process.MyPid());
        });
    }

    internal void RequestResume() => ResumeRequested?.Invoke(this, EventArgs.Empty);

    internal void RequestQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
