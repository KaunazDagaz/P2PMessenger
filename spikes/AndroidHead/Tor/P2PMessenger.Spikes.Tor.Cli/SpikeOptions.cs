using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace P2PMessenger.Spikes.Tor.Cli;

internal sealed record SpikeOptions(string Command)
{
    public const string Usage = """
        Usage: p2pm-tor-spike <command> [options]

        Commands:
          selftest         Checks the control-port client, ADD_ONION with an app key and the SOCKS5 client. Needs no network.
          pair             Starts two tor instances on this machine, publishes an onion service on each and connects them both ways.
          serve            Publishes one onion service with a key kept in the state folder and echoes what it receives, until Ctrl+C.
          dial <address>   Connects to an onion service published by serve, for example on another machine.

        Options:
          --tor <path>         tor executable; default: the newest bundle from get-tor.ps1, then tor on PATH
          --state <folder>     folder for tor data, logs, keys and results.csv; default: spikes/Tor/run
          --name <name>        instance name for serve and dial; default: serve or dial
          --rounds <n>         reconnects over new circuits after the first connection; default: 5
          --pings <n>          round trips on one connection; default: 10
          --connect-timeout <seconds>     how long to keep trying a connection; default: 300
          --bootstrap-timeout <seconds>   how long to wait for tor to bootstrap; default: 300
          --new-key            serve: create a new onion key instead of reusing the stored one
        """;

    private static readonly string[] Commands = ["selftest", "pair", "serve", "dial"];

    public string? TorExecutable { get; init; }

    public string? StateDirectory { get; init; }

    public string? Name { get; init; }

    public string? Target { get; init; }

    public int Rounds { get; init; } = 5;

    public int Pings { get; init; } = 10;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(300);

    public TimeSpan BootstrapTimeout { get; init; } = TimeSpan.FromSeconds(300);

    public bool NewKey { get; init; }

    public static bool TryParse(string[] args, [NotNullWhen(true)] out SpikeOptions? options, out string error)
    {
        options = null;
        error = string.Empty;

        if (args.Length == 0 || !Commands.Contains(args[0], StringComparer.Ordinal))
        {
            error = "Give one of the commands: " + string.Join(", ", Commands) + ".";
            return false;
        }

        var result = new SpikeOptions(args[0]);
        var index = 1;
        if (result.Command == "dial")
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "dial needs the onion address to connect to.";
                return false;
            }

            result = result with { Target = args[1] };
            index = 2;
        }

        for (; index < args.Length; index++)
        {
            var option = args[index];
            if (option == "--new-key")
            {
                result = result with { NewKey = true };
                continue;
            }

            if (index + 1 >= args.Length)
            {
                error = $"{option} needs a value.";
                return false;
            }

            var value = args[++index];
            switch (option)
            {
                case "--tor":
                    result = result with { TorExecutable = value };
                    break;
                case "--state":
                    result = result with { StateDirectory = value };
                    break;
                case "--name":
                    result = result with { Name = value };
                    break;
                case "--rounds" when TryParseCount(value, out var rounds):
                    result = result with { Rounds = rounds };
                    break;
                case "--pings" when TryParseCount(value, out var pings):
                    result = result with { Pings = pings };
                    break;
                case "--connect-timeout" when TryParseCount(value, out var seconds) && seconds > 0:
                    result = result with { ConnectTimeout = TimeSpan.FromSeconds(seconds) };
                    break;
                case "--bootstrap-timeout" when TryParseCount(value, out var seconds) && seconds > 0:
                    result = result with { BootstrapTimeout = TimeSpan.FromSeconds(seconds) };
                    break;
                default:
                    error = $"Unknown option or bad value: {option} {value}";
                    return false;
            }
        }

        options = result;
        return true;
    }

    private static bool TryParseCount(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= 10_000;
}
