using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Tor;

public sealed class OnionDialer : IDialer
{
    private readonly IPEndPoint proxy;
    private readonly string host;
    private readonly int port;
    private readonly ILogger logger;
    private int lastFailure;

    public OnionDialer(IPEndPoint proxy, OnionAddress address, ILogger logger)
        : this(proxy, address?.Host ?? throw new ArgumentNullException(nameof(address)), OnionService.VirtualPort, logger)
    {
    }

    internal OnionDialer(IPEndPoint proxy, string host, int port, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        if (!IPAddress.IsLoopback(proxy.Address))
        {
            throw new ArgumentException("tor's SOCKS port must be on a loopback address.", nameof(proxy));
        }

        if (!Socks5Client.IsValidHost(host))
        {
            throw new ArgumentException("The host name is not one that tor's SOCKS port takes.", nameof(host));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        this.proxy = proxy;
        this.host = host;
        this.port = port;
        this.logger = logger;
    }

    public ConnectionRoute Route => ConnectionRoute.Tor;

    public SocksFailure LastFailure => (SocksFailure)Volatile.Read(ref lastFailure);

    public async ValueTask<Stream?> DialAsync(CancellationToken cancellationToken)
    {
        Socket socket = new(proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(proxy, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return Fail(SocksFailure.ProxyUnreachable);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        NetworkStream stream = new(socket, ownsSocket: true);
        SocksFailure failure;
        try
        {
            failure = await Socks5Client.HandshakeAsync(stream, host, port, Socks5Client.NewIsolation(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            failure = SocksFailure.ProxyClosed;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (failure == SocksFailure.None)
        {
            Volatile.Write(ref lastFailure, (int)SocksFailure.None);
            return stream;
        }

        await stream.DisposeAsync().ConfigureAwait(false);
        return Fail(failure);
    }

    private Stream? Fail(SocksFailure failure)
    {
        Volatile.Write(ref lastFailure, (int)failure);
        logger.OnionDialFailed(failure);
        return null;
    }
}
