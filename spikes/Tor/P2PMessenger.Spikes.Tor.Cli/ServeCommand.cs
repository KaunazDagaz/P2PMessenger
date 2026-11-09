namespace P2PMessenger.Spikes.Tor.Cli;

internal static class ServeCommand
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(SpikeOptions options, CancellationToken cancellationToken)
    {
        var name = options.Name ?? "serve";
        var results = new Results();
        var state = SpikePaths.StateDirectory(options);
        using var key = await LoadOrCreateKeyAsync(Path.Combine(state, name + "-onion-seed"), options.NewKey, cancellationToken).ConfigureAwait(false);

        await using var tor = await TorStarter.StartAndBootstrapAsync(name, options, results, cancellationToken).ConfigureAwait(false);
        await using var echo = new EchoServer();
        echo.ConnectionAccepted += (_, count) => Console.WriteLine($"[{name}] {DateTimeOffset.Now:HH:mm:ss} connection {count} accepted");

        var publication = await TorStarter.PublishAsync(tor, name, key, echo, results, cancellationToken).ConfigureAwait(false);
        await results.SaveAsync(state, "serve", tor.Version, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Onion address: {publication.Address}");
        Console.WriteLine($"On the other machine run: p2pm-tor-spike dial {publication.Address}");
        Console.WriteLine("Press Ctrl+C to stop.");

        var exitCode = 0;
        try
        {
            while (!tor.HasExited)
            {
                await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"[{name}] {DateTimeOffset.Now:HH:mm:ss} tor is running; {echo.Connections} connection(s) so far");
            }

            Console.WriteLine($"[{name}] {DateTimeOffset.Now:HH:mm:ss} tor exited on its own. Its last log lines:{Environment.NewLine}{tor.LogTail()}");
            exitCode = 1;
        }
        catch (OperationCanceledException)
        {
        }

        PairCommand.ReportDescriptorFailures(name, tor);
        Console.WriteLine($"[{name}] {echo.Connections} connection(s) served.");
        return exitCode;
    }

    private static async Task<OnionServiceKey> LoadOrCreateKeyAsync(string path, bool newKey, CancellationToken cancellationToken)
    {
        if (!newKey && File.Exists(path))
        {
            var seed = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            try
            {
                return OnionServiceKey.FromSeed(seed);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(seed);
            }
        }

        var key = OnionServiceKey.Create();
        var bytes = new byte[OnionServiceKey.SeedLength];
        try
        {
            key.ExportSeed(bytes);
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var file = new FileStream(path, options);
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
