using System.Net;
using System.Net.Sockets;

namespace P2PMessenger.Spikes.Tor.Cli;

internal sealed class EchoServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private int _connections;

    public EchoServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public event EventHandler<int>? ConnectionAccepted;

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public int Connections => Volatile.Read(ref _connections);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _acceptLoop.ConfigureAwait(false);
        _listener.Dispose();
        _stop.Dispose();
    }

    private static async Task EchoAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                break;
            }

            var count = Interlocked.Increment(ref _connections);
            ConnectionAccepted?.Invoke(this, count);
            _ = EchoAsync(client, _stop.Token);
        }
    }
}
