namespace P2PMessenger.Application.Background;

public interface IBackgroundService
{
    event EventHandler? ResumeRequested;

    event EventHandler? QuitRequested;

    void StartRunning();

    void StopRunning();

    void ShowUnlockNeeded();

    void EndProcess();
}
