using System.Net;
using System.Net.Sockets;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Tcp;

public sealed class TcpDialer : IDialer
{
    private readonly IPEndPoint remote;

    public TcpDialer(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        this.remote = remote;
    }

    public ConnectionRoute Route => ConnectionRoute.Nearby;

    public async ValueTask<Stream?> DialAsync(CancellationToken cancellationToken)
    {
        Socket socket = new(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(remote, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
