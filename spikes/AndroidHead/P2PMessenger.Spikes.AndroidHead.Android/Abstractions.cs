using Avalonia.Controls;

namespace P2PMessenger.Spikes.AndroidHead.Head;

public interface IDeviceInfo
{
    string Describe();

    void CopyToClipboard(string text);
}

public interface IPermissionService
{
    Task<string> RequestNotificationsAsync();

    Task<string> RequestCameraAsync();

    Task<string> RequestBatteryExemptionAsync();

    string DescribeBattery();
}

public interface IBackgroundService
{
    string Start(ServiceRequest request);

    string Halt();
}

public interface IQrScanner
{
    Task<string> ScanInActivityAsync();

    Control CreateEmbeddedView(Action<string> onText);
}

public interface ITorRuntime
{
    string Start(string command, ServiceRequest request);

    string Halt();
}

public sealed record SpikeServices(
    IDeviceInfo Device,
    IPermissionService Permissions,
    IBackgroundService Background,
    IQrScanner Scanner,
    ITorRuntime Tor);
