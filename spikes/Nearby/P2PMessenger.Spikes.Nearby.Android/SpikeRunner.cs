using System.Globalization;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Net;
using Build = Android.OS.Build;

namespace P2PMessenger.Spikes.Nearby.Android;

internal static class SpikeRunner
{
    private static readonly Lock Gate = new();
    private static CancellationTokenSource? s_running;
    private static string? s_runningName;

    public static string RunFolder(Context context) =>
        Path.Combine(context.ApplicationContext?.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), "run");

    public static void PrintEnvironment(Context context)
    {
        Console.WriteLine($"Device: {Build.Manufacturer} {Build.Model}, Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), {RuntimeInformation.ProcessArchitecture}, page size {System.Environment.SystemPageSize} bytes");
        EnvironmentReport.Print();
        AndroidInterfaces.PrintBoth();
        PrintActiveNetwork(context);
        Console.WriteLine("Steps: switch mobile data off, switch the hotspot on the sharing phone, join the others to it, then press Start node on every device at about the same time.");
    }

    public static void Run(Context context, string name, Func<string, CancellationToken, Task<int>> command)
    {
        var appContext = context.ApplicationContext ?? context;
        CancellationTokenSource cancellation;
        lock (Gate)
        {
            if (s_running is not null)
            {
                Console.WriteLine($"{s_runningName} is still running; press Stop first.");
                return;
            }

            cancellation = new CancellationTokenSource();
            s_running = cancellation;
            s_runningName = name;
        }

        var folder = RunFolder(appContext);
        using (var start = new Intent(appContext, typeof(KeepAliveService)))
        {
            appContext.StartForegroundService(start);
        }

        Console.WriteLine($"=== {name} started at {DateTimeOffset.Now:HH:mm:ss}");
        _ = Task.Run(async () =>
        {
            try
            {
                var exitCode = await command(folder, cancellation.Token).ConfigureAwait(false);
                Console.WriteLine($"=== {name} finished at {DateTimeOffset.Now:HH:mm:ss} with exit code {exitCode}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"=== {name} stopped at {DateTimeOffset.Now:HH:mm:ss}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.WriteLine($"=== {name} failed at {DateTimeOffset.Now:HH:mm:ss}: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                SaveLog(folder, name);
                using (var stop = new Intent(appContext, typeof(KeepAliveService)))
                {
                    appContext.StopService(stop);
                }

                lock (Gate)
                {
                    s_running = null;
                    s_runningName = null;
                }

                cancellation.Dispose();
            }
        });
    }

    public static void Stop()
    {
        CancellationTokenSource? running;
        string? name;
        lock (Gate)
        {
            running = s_running;
            name = s_runningName;
        }

        if (running is null)
        {
            Console.WriteLine("Nothing is running.");
            return;
        }

        Console.WriteLine($"Stopping {name}...");
        try
        {
            _ = running.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void PrintActiveNetwork(Context context)
    {
        try
        {
            if (context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager manager)
            {
                Console.WriteLine("Active network: no ConnectivityManager.");
                return;
            }

            var network = manager.ActiveNetwork;
            if (network is null)
            {
                Console.WriteLine("Active network: none (expected on the phone that shares the hotspot without mobile data or Wi-Fi).");
                return;
            }

            var properties = manager.GetLinkProperties(network);
            var capabilities = manager.GetNetworkCapabilities(network);
            var transports = new List<string>();
            if (capabilities is not null)
            {
                foreach (var transport in new[] { TransportType.Wifi, TransportType.Cellular, TransportType.Bluetooth, TransportType.Ethernet, TransportType.Vpn })
                {
                    if (capabilities.HasTransport(transport))
                    {
                        transports.Add(transport.ToString());
                    }
                }
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Active network: interface {properties?.InterfaceName ?? "?"}, transports {string.Join("+", transports)}, internet {(capabilities?.HasCapability(NetCapability.Internet) == true ? "yes" : "no")}, validated {(capabilities?.HasCapability(NetCapability.Validated) == true ? "yes" : "no")}"));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.WriteLine($"Active network: could not be read ({exception.GetType().Name}: {exception.Message})");
        }
    }

    private static void SaveLog(string folder, string name)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, $"{name}-{stamp}.txt");
            File.WriteAllText(path, SpikeConsole.Snapshot());
            Console.WriteLine($"The log is saved as files/run/{Path.GetFileName(path)} in the app's private storage (spikes/Nearby/README.md shows how to fetch it).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save the log: {exception.Message}");
        }
    }
}
