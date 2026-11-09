using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace P2PMessenger.Spikes.Tor.Cli;

internal static class Probe
{
    public const int VirtualPort = 7000;
    private const int PingLength = 32;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static string NewIsolationToken() => "p2pm-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public static string NormalizeAddress(string address)
    {
        var host = address.Trim().ToLowerInvariant();
        if (host.EndsWith(".onion", StringComparison.Ordinal))
        {
            host = host[..^".onion".Length];
        }

        if (host.Length != 56 || !host.All(c => c is (>= 'a' and <= 'z') or (>= '2' and <= '7')))
        {
            throw new ArgumentException("An onion v3 address has 56 base32 characters, optionally followed by .onion.", nameof(address));
        }

        return host + ".onion";
    }

    public static async Task<ConnectResult> ConnectWithRetriesAsync(IPEndPoint socks, string address, string isolationToken, TimeSpan timeout, string label, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var started = Stopwatch.GetTimestamp();
        var failures = new List<string>();
        try
        {
            while (true)
            {
                var attemptStarted = Stopwatch.GetTimestamp();
                try
                {
                    var stream = await Socks5Client.ConnectAsync(socks, address, VirtualPort, isolationToken, timeoutSource.Token).ConfigureAwait(false);
                    return new ConnectResult(stream, Stopwatch.GetElapsedTime(started), Stopwatch.GetElapsedTime(attemptStarted), failures.Count + 1, failures);
                }
                catch (Exception exception) when (exception is Socks5Exception or IOException or SocketException)
                {
                    var failure = string.Create(CultureInfo.InvariantCulture, $"{exception.Message} after {Stopwatch.GetElapsedTime(attemptStarted).TotalSeconds:0.0} s");
                    failures.Add(failure);
                    Console.WriteLine($"[{label}] attempt {failures.Count} failed: {failure}");
                }

                await Task.Delay(RetryDelay, timeoutSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"No connection to the onion service within {timeout.TotalSeconds:0} s after {failures.Count} failed attempts."));
        }
    }

    public static async Task<IReadOnlyList<TimeSpan>> PingAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var roundTrips = new List<TimeSpan>(count);
        var message = new byte[PingLength];
        var echo = new byte[PingLength];

        for (var i = 0; i < count; i++)
        {
            RandomNumberGenerator.Fill(message);
            var started = Stopwatch.GetTimestamp();
            await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(echo, cancellationToken).ConfigureAwait(false);
            roundTrips.Add(Stopwatch.GetElapsedTime(started));

            if (!message.AsSpan().SequenceEqual(echo))
            {
                throw new InvalidDataException("The echoed bytes differ from the bytes sent.");
            }
        }

        return roundTrips;
    }

    public static async Task MeasureAsync(TorProcess client, string address, string label, SpikeOptions options, Results results, CancellationToken cancellationToken)
    {
        var socks = client.SocksEndPoint ?? throw new InvalidOperationException("tor has no SOCKS listener.");

        Console.WriteLine($"[{label}] connecting to {address} (first connection to this service)");
        var first = await ConnectWithRetriesAsync(socks, address, NewIsolationToken(), options.ConnectTimeout, label, cancellationToken).ConfigureAwait(false);
        await using (first.Stream.ConfigureAwait(false))
        {
            results.Add(label, "first_connect_total", first.Total, string.Create(CultureInfo.InvariantCulture, $"attempts={first.Attempts}"));
            results.Add(label, "first_connect_last_attempt", first.LastAttempt);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{label}] connected after {first.Total.TotalSeconds:0.0} s ({first.Attempts} attempt(s))"));

            var roundTrips = await PingAsync(first.Stream, options.Pings, cancellationToken).ConfigureAwait(false);
            foreach (var roundTrip in roundTrips)
            {
                results.Add(label, "round_trip", roundTrip);
            }
        }

        for (var round = 1; round <= options.Rounds; round++)
        {
            var again = await ConnectWithRetriesAsync(socks, address, NewIsolationToken(), options.ConnectTimeout, label, cancellationToken).ConfigureAwait(false);
            await using (again.Stream.ConfigureAwait(false))
            {
                await PingAsync(again.Stream, 1, cancellationToken).ConfigureAwait(false);
                results.Add(label, "reconnect_new_circuit", again.Total, string.Create(CultureInfo.InvariantCulture, $"attempts={again.Attempts}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{label}] reconnect {round}/{options.Rounds} over a new circuit: {again.Total.TotalSeconds:0.0} s"));
            }
        }
    }
}

internal sealed record ConnectResult(NetworkStream Stream, TimeSpan Total, TimeSpan LastAttempt, int Attempts, IReadOnlyList<string> Failures);
