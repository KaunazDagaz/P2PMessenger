using P2PMessenger.Application.Background;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.Tests;

internal sealed class FakeBackground : IBackgroundService, IBatteryExemption, IMessageNotifier, ISettingsStore
{
    private readonly Dictionary<string, bool> flags = new(StringComparer.Ordinal);

    public event EventHandler? ResumeRequested;

    public event EventHandler? QuitRequested;

    public List<string> Calls { get; } = [];

    public bool IsRunning { get; private set; }

    public bool IsExempt { get; set; }

    public bool IsRestricted { get; set; }

    public bool LiftsRestriction { get; set; }

    public bool GrantsExemption { get; set; }

    public bool IsAllowed { get; set; }

    public bool GrantsNotifications { get; set; }

    public bool IsVisible { get; set; } = true;

    public List<MessageNotice> Shown { get; } = [];

    public BackgroundPorts Ports() => new(this, this, this, this, () => IsVisible);

    public void RequestResume() => ResumeRequested?.Invoke(this, EventArgs.Empty);

    public void RequestQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void StartRunning()
    {
        Record(nameof(StartRunning));
        IsRunning = true;
    }

    public void StoppedByAndroid() => IsRunning = false;

    public void StopRunning()
    {
        Record(nameof(StopRunning));
        IsRunning = false;
    }

    public void ShowUnlockNeeded()
    {
        Record(nameof(ShowUnlockNeeded));
        IsRunning = false;
    }

    public void EndProcess() => Record(nameof(EndProcess));

    public Task<bool> RequestAsync(CancellationToken cancellationToken)
    {
        Record("RequestExemption");
        IsExempt = IsExempt || GrantsExemption;
        return Task.FromResult(IsExempt);
    }

    public Task<bool> OpenSettingsAsync(CancellationToken cancellationToken)
    {
        Record("OpenSettings");
        IsRestricted = IsRestricted && !LiftsRestriction;
        return Task.FromResult(true);
    }

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        Record("RequestNotifications");
        IsAllowed = IsAllowed || GrantsNotifications;
        return Task.FromResult(IsAllowed);
    }

    public void Show(MessageNotice notice)
    {
        lock (Shown)
        {
            Shown.Add(notice);
        }
    }

    public bool ReadFlag(string key, bool fallback)
    {
        lock (flags)
        {
            return flags.TryGetValue(key, out bool value) ? value : fallback;
        }
    }

    public void WriteFlag(string key, bool value)
    {
        lock (flags)
        {
            flags[key] = value;
        }
    }

    public void Clear()
    {
        lock (flags)
        {
            flags.Clear();
        }
    }

    private void Record(string call)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }
    }
}
