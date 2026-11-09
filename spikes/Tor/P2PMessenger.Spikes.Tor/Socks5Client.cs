using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace P2PMessenger.Spikes.Tor;

public static class Socks5Client
{
    private const byte Version = 0x05;
    private const byte NoAuthentication = 0x00;
    private const byte UsernamePassword = 0x02;
    private const byte UsernamePasswordVersion = 0x01;
    private const byte ConnectCommand = 0x01;
    private const byte DomainName = 0x03;
    private const byte IPv4 = 0x01;
    private const byte IPv6 = 0x04;

    public static async Task<NetworkStream> ConnectAsync(IPEndPoint proxy, string host, int port, string? isolationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var hostBytes = Encoding.ASCII.GetBytes(host);
        if (hostBytes.Length > 255)
        {
            throw new ArgumentException("A SOCKS5 host name has at most 255 bytes.", nameof(host));
        }

        byte[]? isolationBytes = null;
        if (isolationToken is not null)
        {
            isolationBytes = Encoding.ASCII.GetBytes(isolationToken);
            if (isolationBytes.Length is 0 or > 255)
            {
                throw new ArgumentException("A SOCKS5 isolation token has 1 to 255 bytes.", nameof(isolationToken));
            }
        }

        var socket = new Socket(proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(proxy, cancellationToken).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            await HandshakeAsync(stream, hostBytes, port, isolationBytes, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task HandshakeAsync(NetworkStream stream, byte[] host, int port, byte[]? isolation, CancellationToken cancellationToken)
    {
        var method = isolation is null ? NoAuthentication : UsernamePassword;
        await stream.WriteAsync(new byte[] { Version, 1, method }, cancellationToken).ConfigureAwait(false);

        var choice = new byte[2];
        await stream.ReadExactlyAsync(choice, cancellationToken).ConfigureAwait(false);
        if (choice[0] != Version || choice[1] != method)
        {
            throw new Socks5Exception("The SOCKS5 proxy did not accept the offered authentication method.");
        }

        if (isolation is not null)
        {
            var authentication = new byte[3 + isolation.Length + 1];
            authentication[0] = UsernamePasswordVersion;
            authentication[1] = (byte)isolation.Length;
            isolation.CopyTo(authentication, 2);
            authentication[2 + isolation.Length] = 1;
            authentication[3 + isolation.Length] = (byte)'x';
            await stream.WriteAsync(authentication, cancellationToken).ConfigureAwait(false);

            var status = new byte[2];
            await stream.ReadExactlyAsync(status, cancellationToken).ConfigureAwait(false);
            if (status[0] != UsernamePasswordVersion || status[1] != 0)
            {
                throw new Socks5Exception("The SOCKS5 proxy rejected the isolation credentials.");
            }
        }

        var request = new byte[7 + host.Length];
        request[0] = Version;
        request[1] = ConnectCommand;
        request[2] = 0;
        request[3] = DomainName;
        request[4] = (byte)host.Length;
        host.CopyTo(request, 5);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5 + host.Length), (ushort)port);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (header[0] != Version)
        {
            throw new Socks5Exception("The SOCKS5 proxy sent a reply with a wrong version.");
        }

        if (header[1] != 0)
        {
            throw new Socks5Exception(header[1]);
        }

        var addressLength = header[3] switch
        {
            IPv4 => 4,
            IPv6 => 16,
            DomainName => await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false),
            _ => throw new Socks5Exception("The SOCKS5 proxy sent a reply with an unknown address type."),
        };

        var boundAddress = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(boundAddress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadByteAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer[0];
    }
}
