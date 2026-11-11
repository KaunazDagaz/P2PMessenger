using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace P2PMessenger.Spikes.Tor.Cli;

internal sealed class FakeSocksServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly byte _replyCode;
    private readonly Task _serve;

    public FakeSocksServer(byte replyCode)
    {
        _replyCode = replyCode;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serve = ServeOneAsync();
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public string? RequestedHost { get; private set; }

    public int RequestedPort { get; private set; }

    public string? Username { get; private set; }

    public async Task WaitAsync(CancellationToken cancellationToken) =>
        await _serve.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _serve.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException)
        {
        }

        _listener.Dispose();
    }

    private async Task ServeOneAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        var stream = client.GetStream();

        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting).ConfigureAwait(false);
        var methods = new byte[greeting[1]];
        await stream.ReadExactlyAsync(methods).ConfigureAwait(false);
        var method = methods.Contains((byte)0x02) ? (byte)0x02 : (byte)0x00;
        await stream.WriteAsync(new byte[] { 0x05, method }).ConfigureAwait(false);

        if (method == 0x02)
        {
            var header = new byte[2];
            await stream.ReadExactlyAsync(header).ConfigureAwait(false);
            var user = new byte[header[1]];
            await stream.ReadExactlyAsync(user).ConfigureAwait(false);
            var passwordLength = new byte[1];
            await stream.ReadExactlyAsync(passwordLength).ConfigureAwait(false);
            var password = new byte[passwordLength[0]];
            await stream.ReadExactlyAsync(password).ConfigureAwait(false);
            Username = Encoding.ASCII.GetString(user);
            await stream.WriteAsync(new byte[] { 0x01, 0x00 }).ConfigureAwait(false);
        }

        var request = new byte[5];
        await stream.ReadExactlyAsync(request).ConfigureAwait(false);
        var host = new byte[request[4]];
        await stream.ReadExactlyAsync(host).ConfigureAwait(false);
        var port = new byte[2];
        await stream.ReadExactlyAsync(port).ConfigureAwait(false);
        RequestedHost = Encoding.ASCII.GetString(host);
        RequestedPort = BinaryPrimitives.ReadUInt16BigEndian(port);

        await stream.WriteAsync(new byte[] { 0x05, _replyCode, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }).ConfigureAwait(false);
        if (_replyCode != 0)
        {
            return;
        }

        var buffer = new byte[256];
        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }
}
