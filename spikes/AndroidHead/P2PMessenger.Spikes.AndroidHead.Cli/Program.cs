using System.Globalization;
using P2PMessenger.Spikes.AndroidHead;
using P2PMessenger.Spikes.AndroidHead.Cli;

const string Usage = """
    Usage: p2pm-androidhead-spike <command> [options]

    Commands:
      selftest                 QR encode and decode round trips, camera-frame parsing, PNG writer and the probe listener over loopback. Needs no camera and no network.
      probe <host>             Sends PING probes to the phone's listener (the app's foreground service) and prints the reply time and the phone's state.
                               --port <n> (45821)  --delay <s> wait before the first probe (0)  --every <s> (60)  --count <n> (1)  --timeout <s> a reply later than this counts as late (60)
      qr <text>                Prints a QR code as text blocks, or writes a PNG with --png <file> [--scale <n>] [--canvas <width>x<height>, the code centred on a larger white image]; --dark for a dark terminal.
      camera                   Lists the computer's cameras (FlashCap) and decodes a QR code from the live picture.
                               --list  --formats  --device <n> (0)  --width <n> preferred frame width (640)  --seconds <n> (20)  --any keep going after the first code
      apk-libs <apk>           Checks that the 64-bit native libraries in an APK are 16 KB-aligned.
    """;

var line = CommandLine.Parse(args);
if (line.Command is not ("selftest" or "probe" or "qr" or "camera" or "apk-libs"))
{
    Console.WriteLine(Usage);
    return 2;
}

SpikeLog.Echo = Console.WriteLine;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var exitCode = 0;
try
{
    exitCode = line.Command switch
    {
        "selftest" => await SelfTestCommand.RunAsync(cancellation.Token).ConfigureAwait(false),
        "probe" => await ProbeCommand.RunAsync(line, cancellation.Token).ConfigureAwait(false),
        "camera" => await CameraCommand.RunAsync(line, cancellation.Token).ConfigureAwait(false),
        "qr" => QrCommand.Run(line),
        _ => ApkLibsCommand.Run(line),
    };
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Stopped.");
    exitCode = 130;
}

if (line.Command is "probe" or "camera" or "selftest")
{
    SaveLog(line.Command);
}

return exitCode;

static void SaveLog(string command)
{
    var folder = FindRunFolder();
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{command}-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
    File.WriteAllText(path, SpikeLog.Snapshot());
    Console.WriteLine($"Log saved: {path}");
}

static string FindRunFolder()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "get-tor.ps1")))
        {
            return Path.Combine(folder.FullName, "run");
        }
    }

    return Path.Combine(Environment.CurrentDirectory, "run");
}
