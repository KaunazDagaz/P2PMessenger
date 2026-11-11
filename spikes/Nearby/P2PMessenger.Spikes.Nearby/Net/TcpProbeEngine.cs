using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record TcpTestResult(bool Success, double ConnectMs, double RttMedianMs, double RttP95Ms, double RttMaxMs, int Pings, string? PeerName, string Error);

public sealed class TcpProbeEngine(NodeOptions options, Action<string, IPAddress> inboundPeer, Action<string> log)
{
    private const int MaxLineLength = 128;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        TcpListener listener = new(IPAddress.Any, options.TcpPort) { ExclusiveAddressUse = false };
        listener.Start(16);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<TcpTestResult> TestAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using TcpClient client = new() { NoDelay = true };
        using CancellationTokenSource connectLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectLimit.CancelAfter(ConnectTimeout);
        long start = Stopwatch.GetTimestamp();
        try
        {
            await client.ConnectAsync(address, port, connectLimit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("no answer within " + (int)ConnectTimeout.TotalSeconds + " s (timeout)");
        }
        catch (SocketException exception)
        {
            return Failed(exception.SocketErrorCode.ToString());
        }

        double connectMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        using CancellationTokenSource ioLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ioLimit.CancelAfter(TimeSpan.FromSeconds(10 + (options.Pings * 0.5)));
        try
        {
            NetworkStream stream = client.GetStream();
            await WriteLineAsync(stream, "HELLO " + options.Name, ioLimit.Token).ConfigureAwait(false);
            string? hello = await ReadLineAsync(stream, ioLimit.Token).ConfigureAwait(false);
            if (hello is null || !hello.StartsWith("HELLO ", StringComparison.Ordinal))
            {
                return Failed("the peer did not answer the greeting", connectMs);
            }

            string peerName = ProbeMessage.SanitizeName(hello[6..]);
            List<double> round = [];
            for (int i = 0; i < options.Pings; i++)
            {
                long pingStart = Stopwatch.GetTimestamp();
                await WriteLineAsync(stream, "PING " + i.ToString(CultureInfo.InvariantCulture), ioLimit.Token).ConfigureAwait(false);
                string? pong = await ReadLineAsync(stream, ioLimit.Token).ConfigureAwait(false);
                if (pong != "PONG " + i.ToString(CultureInfo.InvariantCulture))
                {
                    return Failed("ping " + i + " was not answered", connectMs) with { PeerName = peerName };
                }

                round.Add(Stopwatch.GetElapsedTime(pingStart).TotalMilliseconds);
            }

            await WriteLineAsync(stream, "BYE", ioLimit.Token).ConfigureAwait(false);
            round.Sort();
            return new TcpTestResult(true, connectMs, round[round.Count / 2], round[Math.Min(round.Count - 1, (int)(round.Count * 0.95))], round[^1], round.Count, peerName, string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("the peer stopped answering (timeout)", connectMs);
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            return Failed("connection lost: " + exception.Message, connectMs);
        }
    }

    private static TcpTestResult Failed(string error, double connectMs = 0) => new(false, connectMs, 0, 0, 0, 0, null, error);

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxLineLength];
        byte[] one = new byte[1];
        int length = 0;
        while (length < MaxLineLength)
        {
            int read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            if (one[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(buffer, 0, length);
            }

            buffer[length++] = one[0];
        }

        return null;
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                NetworkStream stream = client.GetStream();
                string? hello = await ReadLineAsync(stream, limit.Token).ConfigureAwait(false);
                if (hello is null || !hello.StartsWith("HELLO ", StringComparison.Ordinal))
                {
                    return;
                }

                await WriteLineAsync(stream, "HELLO " + options.Name, limit.Token).ConfigureAwait(false);
                if (client.Client.RemoteEndPoint is IPEndPoint remote)
                {
                    inboundPeer(ProbeMessage.SanitizeName(hello[6..]), remote.Address);
                }

                while (await ReadLineAsync(stream, limit.Token).ConfigureAwait(false) is { } line && line != "BYE")
                {
                    if (line.StartsWith("PING ", StringComparison.Ordinal))
                    {
                        await WriteLineAsync(stream, "PONG " + line[5..], limit.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                log("A TCP connection ended early: " + exception.GetType().Name);
            }
        }
    }
}
