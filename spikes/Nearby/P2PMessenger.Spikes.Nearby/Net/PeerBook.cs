using System.Net;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record PeerEndpoint(string Name, IPAddress Address, int TcpPort, string Via);

public sealed class PeerBook
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Name, IPAddress Address), PeerEndpoint> _peers = [];

    public bool Learn(string name, IPAddress address, int tcpPort, string via)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue((name, address), out PeerEndpoint? existing))
            {
                if (existing.TcpPort != tcpPort && tcpPort > 0)
                {
                    _peers[(name, address)] = existing with { TcpPort = tcpPort };
                }

                return false;
            }

            _peers[(name, address)] = new PeerEndpoint(name, address, tcpPort, via);
            return true;
        }
    }

    public void Rename(string oldName, string newName, IPAddress address)
    {
        lock (_gate)
        {
            if (_peers.Remove((oldName, address), out PeerEndpoint? existing))
            {
                _peers[(newName, address)] = existing with { Name = newName };
            }
        }
    }

    public IReadOnlyList<PeerEndpoint> Endpoints()
    {
        lock (_gate)
        {
            return [.. _peers.Values.OrderBy(peer => peer.Name, StringComparer.Ordinal).ThenBy(peer => peer.Address.ToString(), StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<string> Names()
    {
        lock (_gate)
        {
            return [.. _peers.Keys.Select(key => key.Name).Where(name => !name.StartsWith('?')).Distinct(StringComparer.Ordinal)];
        }
    }
}
