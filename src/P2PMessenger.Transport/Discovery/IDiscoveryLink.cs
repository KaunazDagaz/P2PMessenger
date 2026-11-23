using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Transport.Discovery;

public interface IDiscoveryLink : IAsyncDisposable
{
    SocketError Open();

    void UseInterfaces(IReadOnlyList<LocalInterface> interfaces);

    ValueTask<DiscoveryDatagram> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    ValueTask<SocketError> SendAsync(LocalInterface from, IPEndPoint destination, ReadOnlyMemory<byte> datagram, bool sweep, CancellationToken cancellationToken);
}
