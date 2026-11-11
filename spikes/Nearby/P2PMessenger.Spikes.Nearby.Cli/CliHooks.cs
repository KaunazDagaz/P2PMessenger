using P2PMessenger.Spikes.Nearby.Net;
using P2PMessenger.Spikes.Nearby.Net.Mdns;

namespace P2PMessenger.Spikes.Nearby.Cli;

internal sealed class CliHooks(bool useMdns) : IPlatformHooks
{
    public IReadOnlyList<LocalInterface>? EnumerateInterfaces() => null;

    public IDisposable? AcquireMulticastLock() => null;

    public IExtraDiscovery? CreateExtraDiscovery() => useMdns ? new MdnsDiscovery() : null;
}
