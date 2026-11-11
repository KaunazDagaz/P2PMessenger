namespace P2PMessenger.Spikes.Tor.Cli;

internal static class PairCommand
{
    public static async Task<int> RunAsync(SpikeOptions options, CancellationToken cancellationToken)
    {
        var results = new Results();
        var state = SpikePaths.StateDirectory(options);

        var startAlice = TorStarter.StartAndBootstrapAsync("alice", options, results, cancellationToken);
        var startBob = TorStarter.StartAndBootstrapAsync("bob", options, results, cancellationToken);
        try
        {
            await Task.WhenAll(startAlice, startBob).ConfigureAwait(false);
        }
        catch
        {
            foreach (var start in new[] { startAlice, startBob }.Where(task => task.IsCompletedSuccessfully))
            {
                await start.Result.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }

        await using var alice = await startAlice.ConfigureAwait(false);
        await using var bob = await startBob.ConfigureAwait(false);
        await using var aliceEcho = new EchoServer();
        await using var bobEcho = new EchoServer();
        using var aliceKey = OnionServiceKey.Create();
        using var bobKey = OnionServiceKey.Create();

        var publishAlice = TorStarter.PublishAsync(alice, "alice", aliceKey, aliceEcho, results, cancellationToken);
        var publishBob = TorStarter.PublishAsync(bob, "bob", bobKey, bobEcho, results, cancellationToken);
        await Task.WhenAll(publishAlice, publishBob).ConfigureAwait(false);
        var aliceService = await publishAlice.ConfigureAwait(false);
        var bobService = await publishBob.ConfigureAwait(false);

        await Probe.MeasureAsync(alice, bobService.Address, "alice->bob", options, results, cancellationToken).ConfigureAwait(false);
        await Probe.MeasureAsync(bob, aliceService.Address, "bob->alice", options, results, cancellationToken).ConfigureAwait(false);

        ReportDescriptorFailures("alice", alice);
        ReportDescriptorFailures("bob", bob);
        results.PrintSummary();
        await results.SaveAsync(state, "pair", alice.Version, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    internal static void ReportDescriptorFailures(string name, TorProcess tor)
    {
        if (tor.DescriptorFailures.Count > 0)
        {
            var reasons = tor.DescriptorFailures.GroupBy(failure => failure.Reason ?? "no reason").Select(group => $"{group.Key} x{group.Count()}");
            Console.WriteLine($"[{name}] descriptor upload or fetch failures: {string.Join(", ", reasons)}");
        }
    }
}
