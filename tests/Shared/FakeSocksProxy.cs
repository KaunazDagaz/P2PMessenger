using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Tests.Shared;

internal sealed class FakeSocksProxy : IAsyncDisposable
{
    private static readonly byte[] SucceededReply = [5, 0, 0, 1, 0, 0, 0, 0, 0, 0];

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<string, OnionTarget> routes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> answers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> usernames = new();
    private readonly ConcurrentQueue<string> hosts = new();
    private readonly ConcurrentDictionary<Socket, byte> open = new();
    private readonly Task acceptLoop;

    public FakeSocksProxy()
    {
        listener.Start();
        acceptLoop = AcceptLoopAsync(stopping.Token);
    }

    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;

    public bool Silent { get; set; }

    public IReadOnlyList<string> Usernames => [.. usernames];

    public IReadOnlyList<string> Hosts => [.. hosts];

    public static IPEndPoint Unused()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (IPEndPoint)probe.LocalEndpoint;
    }

    public int ConnectsTo(OnionAddress address) => hosts.Count(host => host == address.Host);

    public void Route(OnionAddress address, OnionTarget target)
    {
        answers.TryRemove(address.Host, out _);
        routes[address.Host] = target;
    }

    public void Answer(OnionAddress address, byte reply)
    {
        routes.TryRemove(address.Host, out _);
        answers[address.Host] = reply;
    }

    public void CutAll()
    {
        foreach (Socket socket in open.Keys)
        {
            socket.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        CutAll();
        try
        {
            await acceptLoop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        stopping.Dispose();
    }

    private static async Task<Socket?> OpenAsync(OnionTarget target, CancellationToken cancellationToken)
    {
        Socket socket = target.SocketPath is { } path
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            EndPoint endpoint = target.SocketPath is { } socketPath
                ? new UnixDomainSocketEndPoint(socketPath)
                : new IPEndPoint(IPAddress.Loopback, target.Port);
            await socket.ConnectAsync(endpoint, cancellationToken);
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    private static async Task PumpAsync(Socket from, Socket to)
    {
        byte[] buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                int read = await from.ReceiveAsync(buffer, SocketFlags.None);
                if (read == 0)
                {
                    break;
                }

                await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client = await listener.AcceptSocketAsync(cancellationToken);
            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(Socket client, CancellationToken cancellationToken)
    {
        open.TryAdd(client, 0);
        Socket? target = null;
        try
        {
            using NetworkStream stream = new(client, ownsSocket: false);
            byte[] head = new byte[2];
            await stream.ReadExactlyAsync(head, cancellationToken);
            byte[] methods = new byte[head[1]];
            await stream.ReadExactlyAsync(methods, cancellationToken);
            if (head[0] != 5 || !methods.Contains((byte)2))
            {
                await stream.WriteAsync(new byte[] { 5, 0xFF }, cancellationToken);
                return;
            }

            await stream.WriteAsync(new byte[] { 5, 2 }, cancellationToken);
            await stream.ReadExactlyAsync(head, cancellationToken);
            byte[] user = new byte[head[1]];
            await stream.ReadExactlyAsync(user, cancellationToken);
            await stream.ReadExactlyAsync(head.AsMemory(0, 1), cancellationToken);
            byte[] password = new byte[head[0]];
            await stream.ReadExactlyAsync(password, cancellationToken);
            usernames.Enqueue(Encoding.ASCII.GetString(user));
            await stream.WriteAsync(new byte[] { 1, 0 }, cancellationToken);

            byte[] request = new byte[5];
            await stream.ReadExactlyAsync(request, cancellationToken);
            byte[] name = new byte[request[4] + 2];
            await stream.ReadExactlyAsync(name, cancellationToken);
            string host = Encoding.ASCII.GetString(name, 0, request[4]);
            int port = BinaryPrimitives.ReadUInt16BigEndian(name.AsSpan(request[4]));
            hosts.Enqueue(host);
            if (Silent)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (answers.TryGetValue(host, out byte answer))
            {
                await stream.WriteAsync(new byte[] { 5, answer, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken);
                return;
            }

            if (port != OnionService.VirtualPort || !routes.TryGetValue(host, out OnionTarget? route))
            {
                await stream.WriteAsync(new byte[] { 5, 0xF0, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken);
                return;
            }

            target = await OpenAsync(route, cancellationToken);
            if (target is null)
            {
                await stream.WriteAsync(new byte[] { 5, 0xF2, 0, 1, 0, 0, 0, 0, 0, 0 }, cancellationToken);
                return;
            }

            open.TryAdd(target, 0);
            await stream.WriteAsync(SucceededReply, cancellationToken);
            Task up = PumpAsync(client, target);
            Task down = PumpAsync(target, client);
            await Task.WhenAny(up, down);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or EndOfStreamException)
        {
        }
        finally
        {
            open.TryRemove(client, out _);
            client.Dispose();
            if (target is not null)
            {
                open.TryRemove(target, out _);
                target.Dispose();
            }
        }
    }
}
