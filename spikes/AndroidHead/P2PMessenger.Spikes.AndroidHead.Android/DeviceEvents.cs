using Android.Content;
using Android.Net;
using Android.OS;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class DeviceEvents : IDisposable
{
    private readonly Context _context;
    private readonly EventReceiver _receiver;
    private readonly NetworkWatcher _network;
    private readonly ConnectivityManager? _connectivity;

    public DeviceEvents(Context context)
    {
        _context = context;
        _receiver = new EventReceiver(context);
        using var filter = new IntentFilter();
        filter.AddAction(Intent.ActionScreenOn);
        filter.AddAction(Intent.ActionScreenOff);
        filter.AddAction(Intent.ActionPowerConnected);
        filter.AddAction(Intent.ActionPowerDisconnected);
        filter.AddAction(Intent.ActionBatteryLow);
        filter.AddAction(PowerManager.ActionDeviceIdleModeChanged);
        filter.AddAction(PowerManager.ActionPowerSaveModeChanged);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _ = context.RegisterReceiver(_receiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            _ = context.RegisterReceiver(_receiver, filter);
        }

        _network = new NetworkWatcher();
        _connectivity = (ConnectivityManager?)context.GetSystemService(Context.ConnectivityService);
        _connectivity?.RegisterDefaultNetworkCallback(_network);
    }

    public void Dispose()
    {
        _context.UnregisterReceiver(_receiver);
        _connectivity?.UnregisterNetworkCallback(_network);
        _receiver.Dispose();
        _network.Dispose();
    }

    private sealed class EventReceiver(Context context) : BroadcastReceiver
    {
        public override void OnReceive(Context? receiverContext, Intent? intent)
        {
            var action = intent?.Action ?? "?";
            var name = action[(action.LastIndexOf('.') + 1)..];
            SpikeLog.Write($"event: {name}; {DeviceState.Basic(context)}");
        }
    }

    private sealed class NetworkWatcher : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network) => SpikeLog.Write("network: a default network became available");

        public override void OnLost(Network network) => SpikeLog.Write("network: the default network was lost");

        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities networkCapabilities)
        {
            var transports = new List<string>();
            foreach (var transport in new[] { TransportType.Wifi, TransportType.Cellular, TransportType.Ethernet, TransportType.Vpn })
            {
                if (networkCapabilities.HasTransport(transport))
                {
                    transports.Add(transport.ToString());
                }
            }

            var validated = networkCapabilities.HasCapability(NetCapability.Validated) ? "validated" : "not validated";
            SpikeLog.Write($"network: default network is {string.Join("+", transports)}, {validated}");
        }
    }
}
