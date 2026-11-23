using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Transport.Discovery;

public sealed class UdpDiscoveryLink : IDiscoveryLink
{
    private readonly int port;
    private readonly IPAddress multicastGroup;
    private readonly Lock gate = new();
    private readonly Dictionary<IPAddress, Socket> senders = [];
    private readonly Dictionary<IPAddress, Socket> sweepers = [];
    private readonly HashSet<IPAddress> joined = [];
    private Socket? receiver;
    private bool disposed;

    public UdpDiscoveryLink(int port, IPAddress multicastGroup)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        ArgumentNullException.ThrowIfNull(multicastGroup);

        this.port = port;
        this.multicastGroup = multicastGroup;
    }

    public SocketError Open()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                socket.ExclusiveAddressUse = false;
            }

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException exception)
        {
            socket.Dispose();
            return exception.SocketErrorCode;
        }

        lock (gate)
        {
            if (disposed)
            {
                socket.Dispose();
                return SocketError.Shutdown;
            }

            receiver?.Dispose();
            receiver = socket;
            joined.Clear();
        }

        return SocketError.Success;
    }

    public void UseInterfaces(IReadOnlyList<LocalInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        HashSet<IPAddress> current = [.. interfaces.Select(static item => item.Address)];
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            RetireSockets(senders, current);
            RetireSockets(sweepers, current);
            if (receiver is not { } socket)
            {
                return;
            }

            foreach (IPAddress gone in joined.Where(address => !current.Contains(address)).ToList())
            {
                joined.Remove(gone);
                TrySetMembership(socket, SocketOptionName.DropMembership, gone);
            }

            foreach (LocalInterface item in interfaces)
            {
                if (item.SupportsMulticast && !joined.Contains(item.Address) && TrySetMembership(socket, SocketOptionName.AddMembership, item.Address))
                {
                    joined.Add(item.Address);
                }
            }
        }
    }

    public async ValueTask<DiscoveryDatagram> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Socket? socket;
        lock (gate)
        {
            socket = receiver;
        }

        if (socket is null)
        {
            return DiscoveryDatagram.Closed;
        }

        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellationToken).ConfigureAwait(false);
                return result.RemoteEndPoint is IPEndPoint remote
                    ? new DiscoveryDatagram(result.ReceivedBytes, remote.Address)
                    : new DiscoveryDatagram(0, IPAddress.None);
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize or SocketError.Interrupted or SocketError.NetworkReset)
            {
                if (exception.SocketErrorCode == SocketError.MessageSize)
                {
                    return new DiscoveryDatagram(buffer.Length, IPAddress.None);
                }
            }
            catch (ObjectDisposedException)
            {
                return DiscoveryDatagram.Closed;
            }
            catch (SocketException) when (IsDisposed)
            {
                return DiscoveryDatagram.Closed;
            }
        }
    }

    public async ValueTask<SocketError> SendAsync(LocalInterface from, IPEndPoint destination, ReadOnlyMemory<byte> datagram, bool sweep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(destination);

        Socket? socket;
        SocketError setup;
        lock (gate)
        {
            if (disposed)
            {
                return SocketError.Shutdown;
            }

            setup = GetSender(sweep ? sweepers : senders, from, out socket);
        }

        if (socket is null)
        {
            return setup;
        }

        try
        {
            await socket.SendToAsync(datagram, SocketFlags.None, destination, cancellationToken).ConfigureAwait(false);
            return SocketError.Success;
        }
        catch (SocketException exception)
        {
            return exception.SocketErrorCode;
        }
        catch (ObjectDisposedException)
        {
            return SocketError.Shutdown;
        }
    }

    public ValueTask DisposeAsync()
    {
        List<Socket> sockets;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            sockets = [.. senders.Values, .. sweepers.Values];
            if (receiver is not null)
            {
                sockets.Add(receiver);
            }

            senders.Clear();
            sweepers.Clear();
            joined.Clear();
            receiver = null;
        }

        foreach (Socket socket in sockets)
        {
            socket.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static void RetireSockets(Dictionary<IPAddress, Socket> sockets, HashSet<IPAddress> current)
    {
        foreach (IPAddress gone in sockets.Keys.Where(address => !current.Contains(address)).ToList())
        {
            if (sockets.Remove(gone, out Socket? socket))
            {
                socket.Dispose();
            }
        }
    }

    private static SocketError GetSender(Dictionary<IPAddress, Socket> sockets, LocalInterface from, out Socket? socket)
    {
        if (sockets.TryGetValue(from.Address, out socket))
        {
            return SocketError.Success;
        }

        Socket created = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            created.Bind(new IPEndPoint(from.Address, 0));
            created.EnableBroadcast = true;
            created.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            created.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, from.Address.GetAddressBytes());
        }
        catch (SocketException exception)
        {
            created.Dispose();
            socket = null;
            return exception.SocketErrorCode;
        }

        sockets.Add(from.Address, created);
        socket = created;
        return SocketError.Success;
    }

    private bool IsDisposed
    {
        get
        {
            lock (gate)
            {
                return disposed;
            }
        }
    }

    private bool TrySetMembership(Socket socket, SocketOptionName option, IPAddress localAddress)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.IP, option, new MulticastOption(multicastGroup, localAddress));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
