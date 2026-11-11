using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using P2PMessenger.Spikes.AndroidHead.Probe;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal static class ProbeCommand
{
    public static async Task<int> RunAsync(CommandLine line, CancellationToken cancellationToken)
    {
        var host = line.Positional(0);
        if (host is null)
        {
            Console.WriteLine("probe needs the phone's address, for example: probe 192.168.1.20");
            return 2;
        }

        var port = line.Number("port", ProbeProtocol.DefaultPort);
        var delay = TimeSpan.FromSeconds(line.Number("delay", 0));
        var every = TimeSpan.FromSeconds(line.Number("every", 60));
        var count = line.Number("count", 1);
        var timeout = TimeSpan.FromSeconds(line.Number("timeout", 60));

        SpikeLog.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"probing {host}:{port}: first probe after {delay.TotalSeconds:0} s, then every {every.TotalSeconds:0} s, {count} probe(s), a reply must come within {timeout.TotalSeconds:0} s"));
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        var ok = 0;
        var late = 0;
        var failed = 0;
        var worst = TimeSpan.Zero;
        for (var sequence = 1; sequence <= count; sequence++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var client = new TcpClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(timeout);
                await client.ConnectAsync(host, port, attempt.Token).ConfigureAwait(false);
                var connected = Stopwatch.GetElapsedTime(started);
                var stream = client.GetStream();
                var request = ProbeProtocol.FormatRequest(sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(request), attempt.Token).ConfigureAwait(false);
                var reply = await ReadLineAsync(stream, attempt.Token).ConfigureAwait(false);
                var total = Stopwatch.GetElapsedTime(started);
                if (reply is null || !ProbeProtocol.TryParseReply(reply, out var echoed, out _, out _, out var state) || echoed != sequence)
                {
                    failed++;
                    SpikeLog.Write($"probe #{sequence}: FAILED, bad reply");
                    continue;
                }

                worst = total > worst ? total : worst;
                var within = total <= timeout;
                if (within)
                {
                    ok++;
                }
                else
                {
                    late++;
                }

                SpikeLog.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"probe #{sequence}: {(within ? "ok" : "LATE")}, connect {connected.TotalMilliseconds:0} ms, reply {total.TotalMilliseconds:0} ms; {state}"));
            }
            catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                failed++;
                SpikeLog.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"probe #{sequence}: FAILED after {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s ({exception.GetType().Name}: {exception.Message})"));
            }

            if (sequence < count)
            {
                await Task.Delay(every, cancellationToken).ConfigureAwait(false);
            }
        }

        SpikeLog.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"result: {ok} ok, {late} late, {failed} failed; slowest reply {worst.TotalMilliseconds:0} ms"));
        return failed == 0 && late == 0 ? 0 : 1;
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
