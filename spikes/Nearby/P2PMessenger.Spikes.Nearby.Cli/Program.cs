using System.Globalization;
using P2PMessenger.Spikes.Nearby;
using P2PMessenger.Spikes.Nearby.Cli;
using P2PMessenger.Spikes.Nearby.Identification;
using P2PMessenger.Spikes.Nearby.Net;

string[] commands = ["selftest", "bench-caller", "interfaces", "node"];
string command = args.Length > 0 ? args[0] : "selftest";
if (!commands.Contains(command, StringComparer.Ordinal))
{
    Console.WriteLine(CliOptions.Usage);
    return 2;
}

NodeOptions? nodeOptions = null;
bool useMdns = false;
if (command == "node" && !CliOptions.TryParseNode(args, out nodeOptions, out useMdns, out string error))
{
    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
    Console.WriteLine(CliOptions.Usage);
    return 2;
}

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using SpikeLog log = SpikeLog.Start(command == "node" ? "node-" + nodeOptions!.Name : command);
int exitCode;
try
{
    exitCode = command switch
    {
        "bench-caller" => CallerBenchmark.Run(Counts(args)),
        "interfaces" => PrintInterfaces(),
        "node" => await RunNodeAsync(nodeOptions!, useMdns, cancellation.Token).ConfigureAwait(false),
        _ => await SelfTest.RunAsync(cancellation.Token).ConfigureAwait(false),
    };
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Stopped.");
    exitCode = 130;
}

log.Finish(exitCode);
return exitCode;

static int PrintInterfaces()
{
    EnvironmentReport.Print();
    InterfaceReport.Print();
    return 0;
}

static async Task<int> RunNodeAsync(NodeOptions options, bool useMdns, CancellationToken cancellationToken)
{
    using ProbeNode node = new(options with { ResultsFolder = SpikeLog.RunFolder }, new CliHooks(useMdns));
    return await node.RunAsync(cancellationToken).ConfigureAwait(false);
}

static int[]? Counts(string[] args)
{
    int[] counts =
    [
        .. args.Skip(1)
            .Select(text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : 0)
            .Where(value => value > 0),
    ];
    return counts.Length == 0 ? null : counts;
}
