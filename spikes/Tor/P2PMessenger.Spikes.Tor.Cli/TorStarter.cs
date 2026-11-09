using System.Globalization;

namespace P2PMessenger.Spikes.Tor.Cli;

internal static class TorStarter
{
    public static async Task<TorProcess> StartAsync(string name, SpikeOptions options, CancellationToken cancellationToken)
    {
        var state = SpikePaths.StateDirectory(options);
        var executable = SpikePaths.TorExecutable(options);
        var launch = new TorLaunchOptions(executable, Path.Combine(state, name + "-tor"))
        {
            LogFile = Path.Combine(state, name + "-tor.log"),
        };

        Console.WriteLine($"[{name}] starting {executable}");
        var tor = await TorProcess.StartAsync(launch, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[{name}] tor {tor.Version}: control port authenticated with the cookie, ownership taken, SOCKS on {tor.SocksEndPoint}, {(tor.HadCachedState ? "cached directory data found (warm start)" : "no cached directory data (cold start)")}");

        var lastProgress = -1;
        tor.BootstrapProgress += (_, phase) =>
        {
            if (phase.Progress == lastProgress && phase.Warning is null)
            {
                return;
            }

            lastProgress = phase.Progress;
            var warning = phase.Warning is null ? string.Empty : " WARNING: " + phase.Warning;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{name}] bootstrap {phase.Progress}% {phase.Summary}{warning}"));
        };

        return tor;
    }

    public static async Task<TorProcess> StartAndBootstrapAsync(string name, SpikeOptions options, Results results, CancellationToken cancellationToken)
    {
        var tor = await StartAsync(name, options, cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.BootstrapTimeout);
            TimeSpan elapsed;
            try
            {
                elapsed = await tor.WaitForBootstrapAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var phase = tor.LastBootstrapPhase;
                throw new TimeoutException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{name}] tor did not bootstrap within {options.BootstrapTimeout.TotalSeconds:0} s; it stopped at {phase?.Progress ?? 0}% ({phase?.Summary}) {phase?.Warning}. Its last log lines:{Environment.NewLine}{tor.LogTail()}"));
            }

            results.Add(name, tor.HadCachedState ? "bootstrap_warm" : "bootstrap_cold", elapsed);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{name}] bootstrapped in {elapsed.TotalSeconds:0.0} s"));
            return tor;
        }
        catch
        {
            await tor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<OnionPublication> PublishAsync(TorProcess tor, string name, OnionServiceKey key, EchoServer echo, Results results, CancellationToken cancellationToken)
    {
        var publication = await tor.PublishOnionServiceAsync(key, Probe.VirtualPort, echo.EndPoint, cancellationToken).ConfigureAwait(false);
        results.Add(name, "add_onion", publication.AddOnionDuration);
        Console.WriteLine($"[{name}] onion service {publication.Address} added; waiting for the first descriptor upload");

        var uploaded = await publication.WaitForFirstUploadAsync(cancellationToken).ConfigureAwait(false);
        results.Add(name, "descriptor_first_upload", uploaded);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{name}] first descriptor uploaded {uploaded.TotalSeconds:0.0} s after ADD_ONION"));
        return publication;
    }
}
