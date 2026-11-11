using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record ReceivedProbe(ProbeMessage Message, IPEndPoint Remote, IPAddress? Destination, int InterfaceIndex, string ArrivalKind);

public sealed class SendStats
{
    private long _sent;
    private long _errors;

    public long Sent => Interlocked.Read(ref _sent);

    public long Errors => Interlocked.Read(ref _errors);

    public string? LastError { get; private set; }

    public void Succeeded() => Interlocked.Increment(ref _sent);

    public void Failed(string error)
    {
        Interlocked.Increment(ref _errors);
        LastError = error;
    }
}

public sealed class UdpProbeEngine(NodeOptions options, Action<string> log) : IDisposable
{
    private readonly Dictionary<string, Socket> _senders = [];
    private readonly ConcurrentDictionary<string, SendStats> _stats = [];
    private readonly ConcurrentDictionary<string, string> _joins = [];
    private readonly ConcurrentDictionary<string, long> _arrivals = [];
    private readonly Lock _gate = new();
    private Socket? _receiver;
    private IReadOnlyList<LocalInterface> _interfaces = [];
    private bool _packetInformationUnavailable;
    private long _unparseable;
    private long _selfHeard;

    public IReadOnlyDictionary<string, SendStats> Stats => _stats;

    public IReadOnlyDictionary<string, string> Joins => _joins;

    public IReadOnlyDictionary<string, long> Arrivals => _arrivals;

    public long Unparseable => Interlocked.Read(ref _unparseable);

    public long SelfHeard => Interlocked.Read(ref _selfHeard);

    public bool PacketInformationUnavailable => _packetInformationUnavailable;

    public void OpenReceiver(IReadOnlyList<LocalInterface> interfaces)
    {
        _interfaces = interfaces;
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, options.UdpPort));
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _receiver = socket;
        JoinNew(interfaces);
    }

    public void JoinNew(IReadOnlyList<LocalInterface> interfaces)
    {
        _interfaces = interfaces;
        Socket? socket = _receiver;
        if (socket is null)
        {
            return;
        }

        foreach (LocalInterface item in interfaces)
        {
            string key = item.Address.ToString();
            if (_joins.ContainsKey(key))
            {
                continue;
            }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(options.MulticastGroup, item.Address));
                _joins[key] = "joined";
            }
            catch (SocketException exception)
            {
                _joins[key] = "join failed: " + exception.SocketErrorCode;
                log($"Joining {options.MulticastGroup} on {item} failed: {exception.SocketErrorCode}");
            }
        }
    }

    public async Task ReceiveLoopAsync(Func<ReceivedProbe, Task> handler, CancellationToken cancellationToken)
    {
        Socket socket = _receiver ?? throw new InvalidOperationException("The receiver is not open.");
        byte[] buffer = new byte[4096];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                int received;
                IPEndPoint from;
                IPAddress? destination = null;
                int interfaceIndex = 0;
                if (_packetInformationUnavailable)
                {
                    SocketReceiveFromResult plain = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                    received = plain.ReceivedBytes;
                    from = (IPEndPoint)plain.RemoteEndPoint;
                }
                else
                {
                    SocketReceiveMessageFromResult result = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                    received = result.ReceivedBytes;
                    from = (IPEndPoint)result.RemoteEndPoint;
                    destination = result.PacketInformation.Address;
                    interfaceIndex = result.PacketInformation.Interface;
                }

                string kind = Classify(destination);
                if (!ProbeMessage.TryParse(buffer.AsSpan(0, received), out ProbeMessage? message))
                {
                    Interlocked.Increment(ref _unparseable);
                    continue;
                }

                if (message.Node == options.Name)
                {
                    Interlocked.Increment(ref _selfHeard);
                }
                else
                {
                    _arrivals.AddOrUpdate(kind, 1, (_, count) => count + 1);
                }

                await handler(new ReceivedProbe(message, from, destination, interfaceIndex, kind)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or NotSupportedException)
            {
                _packetInformationUnavailable = true;
                log("The platform gives no packet information; going on without the destination address and the interface of each datagram.");
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize or SocketError.Interrupted)
            {
                continue;
            }
        }
    }

    public async Task SendAsync(LocalInterface item, string mechanism, IPAddress destination, byte[] payload, CancellationToken cancellationToken, bool bulk = false)
    {
        SendStats stats = _stats.GetOrAdd(item.Name + "|" + mechanism, _ => new SendStats());
        Socket? socket = GetSender(item, stats, bulk);
        if (socket is null)
        {
            return;
        }

        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, new IPEndPoint(destination, options.UdpPort), cancellationToken).ConfigureAwait(false);
            stats.Succeeded();
        }
        catch (SocketException exception)
        {
            stats.Failed(exception.SocketErrorCode.ToString());
        }
        catch (ObjectDisposedException)
        {
            stats.Failed("closed");
        }
    }

    public void Dispose()
    {
        _receiver?.Dispose();
        lock (_gate)
        {
            foreach (Socket socket in _senders.Values)
            {
                socket.Dispose();
            }

            _senders.Clear();
        }
    }

    private Socket? GetSender(LocalInterface item, SendStats stats, bool bulk)
    {
        string key = bulk ? item.Address + "|bulk" : item.Address.ToString();
        lock (_gate)
        {
            if (_senders.TryGetValue(key, out Socket? existing))
            {
                return existing;
            }

            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(item.Address, 0));
                socket.EnableBroadcast = true;
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, item.Address.GetAddressBytes());
                _senders[key] = socket;
                return socket;
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                stats.Failed("socket setup: " + exception.SocketErrorCode);
                return null;
            }
        }
    }

    private string Classify(IPAddress? destination)
    {
        if (destination is null || destination.Equals(IPAddress.Any))
        {
            return "unknown";
        }

        byte first = destination.GetAddressBytes()[0];
        if (first is >= 224 and <= 239)
        {
            return "multicast";
        }

        if (destination.Equals(IPAddress.Broadcast))
        {
            return "limited-bcast";
        }

        return _interfaces.Any(item => item.Broadcast.Equals(destination)) ? "directed-bcast" : "unicast";
    }
}
