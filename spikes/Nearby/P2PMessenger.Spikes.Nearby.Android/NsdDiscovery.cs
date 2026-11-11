using System.Net;
using Android.Content;
using Android.Net.Nsd;
using P2PMessenger.Spikes.Nearby.Net;

namespace P2PMessenger.Spikes.Nearby.Android;

internal sealed class NsdDiscovery(Context context) : IExtraDiscovery
{
    private const string ServiceType = "_p2pms05._tcp.";
    private const string NamePrefix = "s05-";
    private const int ResolveTimeoutMs = 8000;

    private readonly SemaphoreSlim _resolveGate = new(1, 1);
    private NsdManager? _manager;
    private RegistrationListener? _registration;
    private DiscoveryListener? _discovery;
    private DiscoveryHost? _host;
    private string? _ownName;

    public string Mechanism => "mdns";

    public async Task RunAsync(DiscoveryHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _manager = context.GetSystemService(Context.NsdService) as NsdManager
            ?? throw new InvalidOperationException("NsdManager is not available on this device.");
        using NsdServiceInfo info = new() { ServiceName = NamePrefix + host.NodeName, ServiceType = ServiceType, Port = host.TcpPort };
        _registration = new RegistrationListener(this);
        _discovery = new DiscoveryListener(this);
        _manager.RegisterService(info, NsdProtocol.DnsSd, _registration);
        _manager.DiscoverServices(ServiceType, NsdProtocol.DnsSd, _discovery);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_registration is not null)
            {
                _manager?.UnregisterService(_registration);
            }
        }
        catch (Java.Lang.IllegalArgumentException)
        {
        }

        try
        {
            if (_discovery is not null)
            {
                _manager?.StopServiceDiscovery(_discovery);
            }
        }
        catch (Java.Lang.IllegalArgumentException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private static string PeerNameOf(string serviceName)
    {
        string name = serviceName[NamePrefix.Length..];
        int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        return ProbeMessage.SanitizeName(suffix > 0 ? name[..suffix] : name);
    }

    private void Log(string line) => _host?.Log("mdns (NsdManager): " + line);

    private void OnFound(NsdServiceInfo? info)
    {
        string? name = info?.ServiceName;
        if (info is null || name is null || !name.StartsWith(NamePrefix, StringComparison.Ordinal) || name == _ownName)
        {
            return;
        }

        Log($"service found: {name}");
        _ = Task.Run(() => ResolveAsync(info));
    }

    private async Task ResolveAsync(NsdServiceInfo info)
    {
        await _resolveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA1422, CS0618
            _manager?.ResolveService(info, new ResolveListener(this, done));
#pragma warning restore CA1422, CS0618
            if (await Task.WhenAny(done.Task, Task.Delay(ResolveTimeoutMs)).ConfigureAwait(false) != done.Task)
            {
                Log($"resolving {info.ServiceName} timed out");
            }
        }
        catch (Java.Lang.IllegalArgumentException exception)
        {
            Log($"resolving {info.ServiceName} failed: {exception.Message}");
        }
        finally
        {
            _resolveGate.Release();
        }
    }

    private void OnResolved(NsdServiceInfo? info)
    {
        string? name = info?.ServiceName;
        if (info is null || name is null || !name.StartsWith(NamePrefix, StringComparison.Ordinal))
        {
            return;
        }

#pragma warning disable CA1422, CS0618
        string? address = info.Host?.HostAddress;
#pragma warning restore CA1422, CS0618
        if (address is null || !IPAddress.TryParse(address, out IPAddress? ip))
        {
            Log($"{name} resolved without an IPv4 address ({address ?? "none"})");
            return;
        }

        _host?.PeerFound(PeerNameOf(name), ip, info.Port, "found by NsdManager");
    }

    private sealed class RegistrationListener(NsdDiscovery owner) : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        public void OnServiceRegistered(NsdServiceInfo? serviceInfo)
        {
            owner._ownName = serviceInfo?.ServiceName;
            owner.Log($"registered as {serviceInfo?.ServiceName}");
        }

        public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => owner.Log($"registration failed: {errorCode}");

        public void OnServiceUnregistered(NsdServiceInfo? serviceInfo) => owner.Log("unregistered");

        public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => owner.Log($"unregistration failed: {errorCode}");
    }

    private sealed class DiscoveryListener(NsdDiscovery owner) : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        public void OnDiscoveryStarted(string? serviceType) => owner.Log($"discovery started for {serviceType}");

        public void OnServiceFound(NsdServiceInfo? serviceInfo) => owner.OnFound(serviceInfo);

        public void OnServiceLost(NsdServiceInfo? serviceInfo) => owner.Log($"service lost: {serviceInfo?.ServiceName}");

        public void OnDiscoveryStopped(string? serviceType) => owner.Log("discovery stopped");

        public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode) => owner.Log($"starting discovery failed: {errorCode}");

        public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode) => owner.Log($"stopping discovery failed: {errorCode}");
    }

    private sealed class ResolveListener(NsdDiscovery owner, TaskCompletionSource done) : Java.Lang.Object, NsdManager.IResolveListener
    {
        public void OnServiceResolved(NsdServiceInfo? serviceInfo)
        {
            owner.OnResolved(serviceInfo);
            done.TrySetResult();
        }

        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
            owner.Log($"resolving {serviceInfo?.ServiceName} failed: {errorCode}");
            done.TrySetResult();
        }
    }
}
