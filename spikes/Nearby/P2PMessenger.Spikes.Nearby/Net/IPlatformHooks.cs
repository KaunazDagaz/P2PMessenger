using System.Net;

namespace P2PMessenger.Spikes.Nearby.Net;

public interface IPlatformHooks
{
    IReadOnlyList<LocalInterface>? EnumerateInterfaces();

    IDisposable? AcquireMulticastLock();

    IExtraDiscovery? CreateExtraDiscovery();
}

public interface IExtraDiscovery : IAsyncDisposable
{
    string Mechanism { get; }

    Task RunAsync(DiscoveryHost host, CancellationToken cancellationToken);
}

public sealed class DiscoveryHost(string nodeName, int tcpPort, Func<IReadOnlyList<LocalInterface>> interfaces, Action<string, IPAddress, int, string> peerFound, Action<string> log)
{
    public string NodeName { get; } = nodeName;

    public int TcpPort { get; } = tcpPort;

    public IReadOnlyList<LocalInterface> Interfaces => interfaces();

    public void PeerFound(string peerName, IPAddress address, int peerTcpPort, string detail) => peerFound(peerName, address, peerTcpPort, detail);

    public void Log(string line) => log(line);
}

public sealed class DefaultPlatformHooks : IPlatformHooks
{
    public static DefaultPlatformHooks Instance { get; } = new();

    public IReadOnlyList<LocalInterface>? EnumerateInterfaces() => null;

    public IDisposable? AcquireMulticastLock() => null;

    public IExtraDiscovery? CreateExtraDiscovery() => null;
}
