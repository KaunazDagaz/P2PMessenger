using System.Net;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Transport.Discovery;

public interface IDiscoveryObserver
{
    void Heard(SessionContact contact, IPEndPoint endpoint);

    void NetworkChanged(NetworkSnapshot network);
}
