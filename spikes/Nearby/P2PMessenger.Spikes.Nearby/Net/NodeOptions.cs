using System.Net;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record NodeOptions
{
    public const int DefaultTcpPort = 47800;
    public const int DefaultUdpPort = 47801;

    public string Name { get; init; } = ProbeMessage.SanitizeName(Environment.MachineName);

    public int TcpPort { get; init; } = DefaultTcpPort;

    public int UdpPort { get; init; } = DefaultUdpPort;

    public IPAddress MulticastGroup { get; init; } = IPAddress.Parse("239.255.83.5");

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(90);

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyList<string> InterfaceFilters { get; init; } = [];

    public IReadOnlyList<IPAddress> ManualPeers { get; init; } = [];

    public int SweepMaxHosts { get; init; } = 510;

    public int Pings { get; init; } = 20;

    public bool UseMdns { get; init; }

    public string? ResultsFolder { get; init; }
}
