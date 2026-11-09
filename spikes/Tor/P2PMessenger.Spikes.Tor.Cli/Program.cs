namespace P2PMessenger.Spikes.Tor.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!SpikeOptions.TryParse(args, out var options, out var error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(SpikeOptions.Usage).ConfigureAwait(false);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return options.Command switch
            {
                "selftest" => await SelfTestCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                "pair" => await PairCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                "serve" => await ServeCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                "dial" => await DialCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => 2,
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine("Stopped.");
            return 130;
        }
        catch (Exception exception) when (exception is TimeoutException or TorControlException or Socks5Exception or IOException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            await Console.Error.WriteLineAsync($"{exception.GetType().Name}: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
