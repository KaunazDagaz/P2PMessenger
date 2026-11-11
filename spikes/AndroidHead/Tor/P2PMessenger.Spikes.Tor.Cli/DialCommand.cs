namespace P2PMessenger.Spikes.Tor.Cli;

internal static class DialCommand
{
    public static async Task<int> RunAsync(SpikeOptions options, CancellationToken cancellationToken)
    {
        var name = options.Name ?? "dial";
        var address = Probe.NormalizeAddress(options.Target!);
        var results = new Results();
        var state = SpikePaths.StateDirectory(options);

        await using var tor = await TorStarter.StartAndBootstrapAsync(name, options, results, cancellationToken).ConfigureAwait(false);
        await Probe.MeasureAsync(tor, address, name, options, results, cancellationToken).ConfigureAwait(false);

        results.PrintSummary();
        await results.SaveAsync(state, "dial", tor.Version, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
