using System.Net;

namespace P2PMessenger.Transport.Discovery;

public readonly record struct DiscoveryDatagram(int Length, IPAddress? Source)
{
    public bool IsClosed => Source is null;

    public static DiscoveryDatagram Closed => default;
}
