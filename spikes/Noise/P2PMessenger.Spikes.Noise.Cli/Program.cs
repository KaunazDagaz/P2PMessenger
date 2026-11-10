using System.Globalization;
using P2PMessenger.Spikes.Noise;
using P2PMessenger.Spikes.Noise.Cli;

string command = args.Length > 0 ? args[0] : "selftest";
if (command is not ("selftest" or "bench" or "android-libs"))
{
    Console.WriteLine("Usage: p2pm-noise-spike [selftest | bench [handshakes] [messages] | android-libs [NuGet packages folder]]");
    return 2;
}

using SpikeLog log = SpikeLog.Start(command);
int exitCode = command switch
{
    "bench" => Benchmark.Run(Number(args, 1, Benchmark.DefaultHandshakes), Number(args, 2, Benchmark.DefaultMessages)),
    "android-libs" => AndroidLibraryCheck.Run(args.Length > 1 ? args[1] : null),
    _ => SelfTest.Run(),
};
log.Finish(exitCode);
return exitCode;

static int Number(string[] args, int index, int fallback)
{
    return args.Length > index && int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
        ? value
        : fallback;
}
