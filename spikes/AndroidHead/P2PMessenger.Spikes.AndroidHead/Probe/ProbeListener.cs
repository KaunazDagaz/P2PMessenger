using System.Net;
using System.Net.Sockets;
using System.Text;

namespace P2PMessenger.Spikes.AndroidHead.Probe;

public sealed class ProbeListener : IAsyncDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly Func<string> _state;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loop;
    private int _answered;

    public ProbeListener(IPEndPoint endPoint, Func<string> state)
    {
        _listener = new TcpListener(endPoint);
        _state = state;
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public int Answered => Volatile.Read(ref _answered);

    public void Start()
    {
        _listener.Start(16);
        _loop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException exception)
            {
                SpikeLog.Write($"listener: accept failed ({exception.SocketErrorCode})");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectionTimeout);
                var stream = client.GetStream();
                var line = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
                if (line is null || !ProbeProtocol.TryParseRequest(line, out var sequence, out var sentUnixMs))
                {
                    SpikeLog.Write($"listener: bad request from {remote}");
                    return;
                }

                var received = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var reply = ProbeProtocol.FormatReply(sequence, sentUnixMs, received, _state()) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), timeout.Token).ConfigureAwait(false);
                var count = Interlocked.Increment(ref _answered);
                SpikeLog.Write($"listener: probe #{sequence} from {remote} answered (total {count}); sender's clock was {received - sentUnixMs} ms behind the receipt");
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                SpikeLog.Write($"listener: connection from {remote} ended ({exception.GetType().Name})");
            }
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[ProbeProtocol.MaxLineLength];
        var length = 0;
        var one = new byte[1];
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            if (one[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\r');
            }

            buffer[length++] = one[0];
        }

        return null;
    }
}
