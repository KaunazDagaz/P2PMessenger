using System.Runtime.InteropServices;
using Android.Content;
using P2PMessenger.Spikes.Tor.Cli;
using Build = Android.OS.Build;

namespace P2PMessenger.Spikes.Tor.Android;

internal static class SpikeRunner
{
    private static readonly string[] TorFileNames = ["libTor.so", "libtor.so"];
    private static readonly Lock Gate = new();
    private static CancellationTokenSource? s_running;
    private static string? s_runningName;

    public static void PrintEnvironment(Context context)
    {
        Console.WriteLine($"Device: {Build.Manufacturer} {Build.Model}, Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), {RuntimeInformation.ProcessArchitecture}, page size {System.Environment.SystemPageSize} bytes");

        var folder = context.ApplicationInfo?.NativeLibraryDir;
        Console.WriteLine($"Native library folder: {folder ?? "none"}");
        if (folder is not null && Directory.Exists(folder))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
                {
                    var mode = Convert.ToString((int)File.GetUnixFileMode(file), 8);
                    Console.WriteLine($"  {Path.GetFileName(file)}, {new FileInfo(file).Length} bytes, mode {mode}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  The folder cannot be listed: {exception.Message}");
            }
        }

        var tor = FindTor(context);
        Console.WriteLine(tor is null
            ? "No tor binary in that folder: Android did not extract it at install."
            : $"tor binary: {tor}");
        Console.WriteLine("Run Self-test first, then Pair, Serve or Dial.");
    }

    public static void Run(Context context, string name, Func<SpikeOptions, CancellationToken, Task<int>> command)
    {
        var appContext = context.ApplicationContext ?? context;
        var tor = FindTor(appContext);
        if (tor is null)
        {
            Console.WriteLine("No tor binary to start; see the lines at the top of the log.");
            return;
        }

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

        var state = Path.Combine(appContext.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), "run");
        var options = new SpikeOptions(name) { TorExecutable = tor, StateDirectory = state };

        using (var start = new Intent(appContext, typeof(KeepAliveService)))
        {
            appContext.StartForegroundService(start);
        }

        Console.WriteLine($"=== {name} started at {DateTimeOffset.Now:HH:mm:ss}");
        _ = Task.Run(async () =>
        {
            try
            {
                var exitCode = await command(options, cancellation.Token).ConfigureAwait(false);
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
                SaveLog(state);
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

    private static string? FindTor(Context context)
    {
        var folder = context.ApplicationInfo?.NativeLibraryDir;
        return folder is null
            ? null
            : TorFileNames.Select(fileName => Path.Combine(folder, fileName)).FirstOrDefault(File.Exists);
    }

    private static void SaveLog(string state)
    {
        try
        {
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "console.txt"), SpikeConsole.Snapshot());
            Console.WriteLine("results.csv, the tor logs and console.txt are in the app's files/run folder (spikes/Tor/README.md, section 6, shows how to fetch them)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save the log: {exception.Message}");
        }
    }
}
