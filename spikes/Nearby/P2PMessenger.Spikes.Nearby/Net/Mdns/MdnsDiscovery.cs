using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Spikes.Nearby.Net.Mdns;

public sealed class MdnsDiscovery : IExtraDiscovery
{
    public const string ServiceType = "_p2pms05._tcp.local";

    private const int MdnsPort = 5353;
    private const string NamePrefix = "s05-";

    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Dictionary<string, (string Target, int Port)> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPAddress> _addresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _joined = [];
    private readonly Lock _stateGate = new();
    private Socket? _socket;
    private DiscoveryHost? _host;
    private string _instanceName = string.Empty;
    private string _hostName = string.Empty;

    public string Mechanism => "mdns";

    public async Task RunAsync(DiscoveryHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _instanceName = NamePrefix + host.NodeName.Replace('.', '_') + "." + ServiceType;
        _hostName = host.NodeName.Replace('.', '_') + ".local";
        try
        {
            OpenSocket();
        }
        catch (SocketException exception)
        {
            host.Log($"mdns: UDP port {MdnsPort} cannot be opened ({exception.SocketErrorCode}); mDNS is off on this device");
            return;
        }

        try
        {
            await Task.WhenAll(ReceiveLoopAsync(cancellationToken), AnnounceLoopAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket?.Dispose();
        _sendGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string PeerNameOf(string instance)
    {
        string label = instance.Split('.')[0];
        string name = label.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase) ? label[NamePrefix.Length..] : label;
        int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        return ProbeMessage.SanitizeName(suffix > 0 ? name[..suffix] : name);
    }

    private void OpenSocket()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        JoinNew();
    }

    private void JoinNew()
    {
        Socket? socket = _socket;
        if (socket is null || _host is null)
        {
            return;
        }

        foreach (LocalInterface item in _host.Interfaces)
        {
            if (!_joined.Add(item.Address.ToString()))
            {
                continue;
            }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, item.Address));
            }
            catch (SocketException exception)
            {
                _host.Log($"mdns: joining {Group} on {item} failed: {exception.SocketErrorCode}");
            }
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        int round = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            JoinNew();
            await SendQueryAsync(ServiceType, DnsType.Reference, cancellationToken).ConfigureAwait(false);
            if (round % 5 == 0)
            {
                await SendResponseAsync(null, cancellationToken).ConfigureAwait(false);
            }

            round++;
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Socket socket = _socket ?? throw new InvalidOperationException("The mDNS socket is not open.");
        byte[] buffer = new byte[9000];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SocketReceiveMessageFromResult result = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                if (DnsMessage.TryParse(buffer.AsSpan(0, result.ReceivedBytes), out DnsMessage? message))
                {
                    await HandleAsync(message, result.PacketInformation.Interface, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize or SocketError.Interrupted)
            {
                continue;
            }
        }
    }

    private async Task HandleAsync(DnsMessage message, int interfaceIndex, CancellationToken cancellationToken)
    {
        if (!message.IsResponse)
        {
            LocalInterface? arrival = _host?.Interfaces.FirstOrDefault(item => item.Index == interfaceIndex && interfaceIndex != 0);
            foreach (DnsQuestion question in message.Questions)
            {
                bool forService = DnsMessage.NameEquals(question.Name, ServiceType) && question.Type is DnsType.Reference or DnsType.Any;
                bool forInstance = DnsMessage.NameEquals(question.Name, _instanceName) && question.Type is DnsType.Srv or DnsType.Txt or DnsType.Any;
                bool forHost = DnsMessage.NameEquals(question.Name, _hostName) && question.Type is DnsType.A or DnsType.Any;
                if (forService || forInstance || forHost)
                {
                    await SendResponseAsync(arrival, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            return;
        }

        List<string> followUps = [];
        lock (_stateGate)
        {
            foreach (DnsRecord record in message.Records)
            {
                switch (record.Type)
                {
                    case DnsType.Reference when DnsMessage.NameEquals(record.Name, ServiceType) && record.Target is not null:
                        _instances.Add(record.Target);
                        break;
                    case DnsType.Srv when record.Target is not null:
                        _services[record.Name] = (record.Target, record.Port);
                        break;
                    case DnsType.A when record.Address is not null:
                        _addresses[record.Name] = record.Address;
                        break;
                    default:
                        break;
                }
            }

            foreach (string instance in _instances.Where(name => !DnsMessage.NameEquals(name, _instanceName)))
            {
                if (!_services.TryGetValue(instance, out (string Target, int Port) service))
                {
                    followUps.Add(instance + "|" + DnsType.Srv);
                }
                else if (!_addresses.TryGetValue(service.Target, out IPAddress? address))
                {
                    followUps.Add(service.Target + "|" + DnsType.A);
                }
                else if (_reported.Add(instance + "|" + address))
                {
                    _host?.PeerFound(PeerNameOf(instance), address, service.Port, "found by own mDNS");
                }
            }
        }

        foreach (string followUp in followUps)
        {
            string[] parts = followUp.Split('|');
            await SendQueryAsync(parts[0], ushort.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendQueryAsync(string name, ushort type, CancellationToken cancellationToken)
    {
        byte[] bytes = new DnsMessage(false, [new DnsQuestion(name, type)], []).Write();
        await SendAsync(_host?.Interfaces ?? [], _ => bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendResponseAsync(LocalInterface? only, CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalInterface> targets = only is not null ? [only] : _host?.Interfaces ?? [];
        int port = _host?.TcpPort ?? 0;
        await SendAsync(
            targets,
            item => new DnsMessage(
                true,
                [],
                [
                    new DnsRecord(ServiceType, DnsType.Reference, Target: _instanceName),
                    new DnsRecord(_instanceName, DnsType.Srv, Target: _hostName, Port: port),
                    new DnsRecord(_instanceName, DnsType.Txt),
                    new DnsRecord(_hostName, DnsType.A, Address: item.Address),
                ]).Write(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(IReadOnlyList<LocalInterface> targets, Func<LocalInterface, byte[]> build, CancellationToken cancellationToken)
    {
        Socket? socket = _socket;
        if (socket is null)
        {
            return;
        }

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (LocalInterface item in targets)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, item.Address.GetAddressBytes());
                    await socket.SendToAsync(build(item), SocketFlags.None, new IPEndPoint(Group, MdnsPort), cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException exception)
                {
                    _host?.Log($"mdns: sending on {item} failed: {exception.SocketErrorCode}");
                }
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
