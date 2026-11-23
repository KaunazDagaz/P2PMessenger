using P2PMessenger.Transport.Discovery;

namespace P2PMessenger.Application.Connections;

public sealed record DiscoverySetup
{
    public DiscoveryOptions Options { get; init; } = new();

    public ILocalInterfaceSource Interfaces { get; init; } = new SystemInterfaceSource();

    public Func<DiscoveryOptions, IDiscoveryLink> Links { get; init; } = static options => new UdpDiscoveryLink(options.Port, options.MulticastGroup);

    public static DiscoverySetup LocalNetwork(ILocalInterfaceSource? interfaces) =>
        interfaces is null ? new DiscoverySetup() : new DiscoverySetup { Interfaces = interfaces };
}
