using System.Globalization;

namespace P2PMessenger.Spikes.Tor.Cli;

internal static class SelfTestCommand
{
    private const string MalformedOnion = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.onion";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    public static async Task<int> RunAsync(SpikeOptions options, CancellationToken cancellationToken)
    {
        var checks = new Checks(cancellationToken);
        var state = SpikePaths.StateDirectory(options);
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var firstName = $"selftest-{runId}-a";
        var secondName = $"selftest-{runId}-b";

        await checks.RunAsync("Event parser reads a bootstrap phase with a quoted, escaped summary", () =>
        {
            var parsed = TorEvents.TryParseBootstrapPhase("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=100 TAG=done SUMMARY=\"Done \\\"x\\\" \\\\ \\303\\251\"", out var phase);
            return Task.FromResult(parsed && phase!.Progress == 100 && phase.Tag == "done" && phase.Summary == "Done \"x\" \\ é");
        }).ConfigureAwait(false);

        await checks.RunAsync("Event parser reads an HS_DESC UPLOADED event", () =>
        {
            var parsed = TorEvents.TryParseHiddenServiceDescriptor("HS_DESC UPLOADED abcdef UNKNOWN $AAAA~relay", out var descriptorEvent);
            return Task.FromResult(parsed && descriptorEvent!.Action == "UPLOADED" && descriptorEvent.Address == "abcdef");
        }).ConfigureAwait(false);

        await checks.RunAsync("SOCKS5 client: CONNECT by host name succeeds and data flows", async () =>
        {
            await using var proxy = new FakeSocksServer(0x00);
            await using var stream = await Socks5Client.ConnectAsync(proxy.EndPoint, MalformedOnion, Probe.VirtualPort, null, cancellationToken).ConfigureAwait(false);
            var roundTrips = await Probe.PingAsync(stream, 3, cancellationToken).ConfigureAwait(false);
            return roundTrips.Count == 3 && proxy.RequestedHost == MalformedOnion && proxy.RequestedPort == Probe.VirtualPort && proxy.Username is null;
        }).ConfigureAwait(false);

        await checks.RunAsync("SOCKS5 client: isolation token is sent as the SOCKS username", async () =>
        {
            await using var proxy = new FakeSocksServer(0x00);
            await using var stream = await Socks5Client.ConnectAsync(proxy.EndPoint, MalformedOnion, Probe.VirtualPort, "p2pm-test", cancellationToken).ConfigureAwait(false);
            await Probe.PingAsync(stream, 1, cancellationToken).ConfigureAwait(false);
            return proxy.Username == "p2pm-test";
        }).ConfigureAwait(false);

        await checks.RunAsync("SOCKS5 client: tor's extended reply 0xF0 becomes an error with that code", async () =>
        {
            await using var proxy = new FakeSocksServer(0xF0);
            var code = await ReplyCodeAsync(proxy.EndPoint, MalformedOnion, cancellationToken).ConfigureAwait(false);
            return code == 0xF0;
        }).ConfigureAwait(false);

        using var key = OnionServiceKey.Create();
        await using var echo = new EchoServer();
        string? firstServiceId = null;

        try
        {
            await using (var tor = await TorStarter.StartAsync(firstName, options, cancellationToken).ConfigureAwait(false))
            {
                checks.Record("tor started; control port authenticated with the cookie; ownership taken", true, "tor " + tor.Version);
                checks.Record("tor opened a SOCKS listener", tor.SocksEndPoint is not null, tor.SocksEndPoint?.ToString());

                await checks.RunAsync("ADD_ONION accepts a key made by the app and returns an address", async () =>
                {
                    var publication = await tor.PublishOnionServiceAsync(key, Probe.VirtualPort, echo.EndPoint, cancellationToken).ConfigureAwait(false);
                    firstServiceId = publication.ServiceId;
                    return publication.ServiceId.Length == 56;
                }).ConfigureAwait(false);

                await checks.RunAsync("DEL_ONION, then ADD_ONION with the same key gives the same address", async () =>
                {
                    await tor.Control.DeleteOnionAsync(firstServiceId!, cancellationToken).ConfigureAwait(false);
                    var again = await tor.PublishOnionServiceAsync(key, Probe.VirtualPort, echo.EndPoint, cancellationToken).ConfigureAwait(false);
                    return again.ServiceId == firstServiceId;
                }).ConfigureAwait(false);

                await checks.RunAsync("tor answers a SOCKS request for a malformed onion address with 0xF6", async () =>
                {
                    var code = await ReplyCodeAsync(tor.SocksEndPoint!, MalformedOnion, cancellationToken).ConfigureAwait(false);
                    return code == 0xF6;
                }).ConfigureAwait(false);
            }

            var dataDirectory = Path.Combine(state, firstName + "-tor");
            var keyFiles = Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).StartsWith("hs_ed25519", StringComparison.Ordinal))
                .ToList();
            checks.Record("tor's data directory holds no onion service key after the run", keyFiles.Count == 0, keyFiles.Count == 0 ? null : string.Join(", ", keyFiles));

            await using (var fresh = await TorStarter.StartAsync(secondName, options, cancellationToken).ConfigureAwait(false))
            {
                await checks.RunAsync("A second tor with an empty data directory gives the same address for the same app key", async () =>
                {
                    var publication = await fresh.PublishOnionServiceAsync(key, Probe.VirtualPort, echo.EndPoint, cancellationToken).ConfigureAwait(false);
                    return publication.ServiceId == firstServiceId;
                }).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is TorControlException or IOException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            checks.Record("tor could be started and controlled", false, exception.Message);
        }
        finally
        {
            foreach (var name in new[] { firstName, secondName })
            {
                TryDelete(Path.Combine(state, name + "-tor"));
            }
        }

        return checks.Report();
    }

    private static async Task<byte?> ReplyCodeAsync(System.Net.IPEndPoint proxy, string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            await using var stream = await Socks5Client.ConnectAsync(proxy, host, Probe.VirtualPort, null, timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Socks5Exception exception)
        {
            return exception.ReplyCode;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not remove {directory}: {exception.Message}");
        }
    }

    private sealed class Checks(CancellationToken cancellationToken)
    {
        private int _passed;
        private int _failed;

        public async Task RunAsync(string name, Func<Task<bool>> check)
        {
            try
            {
                Record(name, await check().ConfigureAwait(false), null);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                Record(name, false, exception.GetType().Name + ": " + exception.Message);
            }
        }

        public void Record(string name, bool passed, string? detail)
        {
            if (passed)
            {
                _passed++;
            }
            else
            {
                _failed++;
            }

            var suffix = string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})";
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{suffix}");
        }

        public int Report()
        {
            Console.WriteLine();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{_passed} passed, {_failed} failed."));
            return _failed == 0 ? 0 : 1;
        }
    }
}
