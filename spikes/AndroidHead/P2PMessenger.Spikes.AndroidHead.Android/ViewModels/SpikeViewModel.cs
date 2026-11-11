using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Spikes.AndroidHead.Probe;

namespace P2PMessenger.Spikes.AndroidHead.Head.ViewModels;

public sealed partial class SpikeViewModel : ObservableObject, IDisposable
{
    private const int LogCharacters = 24_000;

    private readonly SpikeServices _services;
    private int _refreshPending;

    [ObservableProperty]
    private string _log;

    [ObservableProperty]
    private ServiceKind _selectedKind = ServiceKind.ConnectedDevice;

    [ObservableProperty]
    private bool _useWakeLock;

    [ObservableProperty]
    private string _port = ProbeProtocol.DefaultPort.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isScannerVisible;

    [ObservableProperty]
    private bool _isBusy;

    public SpikeViewModel(SpikeServices services)
    {
        _services = services;
        _log = SpikeLog.Tail(LogCharacters);
        SpikeLog.LineWritten += OnLineWritten;
        AutoRun.ViewCommand += OnViewCommand;
    }

    public IReadOnlyList<ServiceKind> Kinds { get; } = Enum.GetValues<ServiceKind>();

    public void Dispose()
    {
        SpikeLog.LineWritten -= OnLineWritten;
        AutoRun.ViewCommand -= OnViewCommand;
    }

    public void OnScanText(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SpikeLog.Write($"embedded scanner: {text.Length} characters: {text}");
            IsScannerVisible = false;
        });
    }

    private void OnViewCommand(string command) =>
        Dispatcher.UIThread.Post(() => IsScannerVisible = command == "scan-view");

    private ServiceRequest Request() =>
        new(SelectedKind, UseWakeLock, int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and < 65536 ? port : ProbeProtocol.DefaultPort);

    private async Task RunAsync(Func<Task<string>> action)
    {
        IsBusy = true;
        try
        {
            SpikeLog.Write(await action().ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SpikeLog.Write($"failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLineWritten(object? sender, string line)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _refreshPending, 0);
                Log = SpikeLog.Tail(LogCharacters);
            });
        }
    }

    [RelayCommand]
    private void ShowInfo() => SpikeLog.Write(_services.Device.Describe());

    [RelayCommand]
    private Task NotificationsAsync() => RunAsync(_services.Permissions.RequestNotificationsAsync);

    [RelayCommand]
    private Task CameraPermissionAsync() => RunAsync(_services.Permissions.RequestCameraAsync);

    [RelayCommand]
    private Task BatteryAsync() => RunAsync(_services.Permissions.RequestBatteryExemptionAsync);

    [RelayCommand]
    private void StartService() => SpikeLog.Write(_services.Background.Start(Request()));

    [RelayCommand]
    private void StopService() => SpikeLog.Write(_services.Background.Halt());

    [RelayCommand]
    private void ToggleScanner() => IsScannerVisible = !IsScannerVisible;

    [RelayCommand]
    private Task ScanActivityAsync() => RunAsync(_services.Scanner.ScanInActivityAsync);

    [RelayCommand]
    private void TorSelfTest() => SpikeLog.Write(_services.Tor.Start("selftest", Request()));

    [RelayCommand]
    private void TorPair() => SpikeLog.Write(_services.Tor.Start("pair", Request()));

    [RelayCommand]
    private void TorServe() => SpikeLog.Write(_services.Tor.Start("serve", Request()));

    [RelayCommand]
    private void TorStop() => SpikeLog.Write(_services.Tor.Halt());

    [RelayCommand]
    private void CopyLog()
    {
        _services.Device.CopyToClipboard(SpikeLog.Snapshot());
        SpikeLog.Write("The log was copied to the clipboard.");
    }
}
