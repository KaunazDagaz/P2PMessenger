using P2PMessenger.Application.Background;

namespace P2PMessenger.UI.Services;

public sealed class BackgroundPorts
{
    public BackgroundPorts(IBackgroundService service, IBatteryExemption battery, IMessageNotifier notifier, ISettingsStore settings, Func<bool> isAppVisible)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isAppVisible);

        Service = service;
        Battery = battery;
        Notifier = notifier;
        Settings = new BackgroundSettings(settings);
        IsAppVisible = isAppVisible;
    }

    public IBackgroundService Service { get; }

    public IBatteryExemption Battery { get; }

    public IMessageNotifier Notifier { get; }

    public BackgroundSettings Settings { get; }

    public Func<bool> IsAppVisible { get; }

    public bool ShouldOffer => !Settings.WasOffered && (!Battery.IsExempt || !Notifier.IsAllowed);
}
