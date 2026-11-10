using System.Globalization;
using System.Runtime.InteropServices;
using Android.Content;
using Build = Android.OS.Build;

namespace P2PMessenger.Spikes.Noise.Android;

internal static class SpikeRunner
{
    private static readonly Lock Gate = new();
    private static bool s_running;

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
                    Console.WriteLine($"  {Path.GetFileName(file)}, {new FileInfo(file).Length} bytes");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  The folder cannot be listed: {exception.Message}");
            }
        }

        Console.WriteLine("Press Self-test, then Benchmark.");
    }

    public static void Run(Context context, string name, Func<int> command)
    {
        lock (Gate)
        {
            if (s_running)
            {
                Console.WriteLine("A run is still going; wait for it to finish.");
                return;
            }

            s_running = true;
        }

        var appContext = context.ApplicationContext ?? context;
        var folder = Path.Combine(appContext.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), "run");
        Console.WriteLine($"=== {name} started");
        _ = Task.Run(() =>
        {
            try
            {
                var exitCode = command();
                Console.WriteLine($"=== {name} finished with exit code {exitCode}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.WriteLine($"=== {name} failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                SaveLog(folder, name);
                lock (Gate)
                {
                    s_running = false;
                }
            }
        });
    }

    private static void SaveLog(string folder, string name)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, $"{name}-{stamp}.txt");
            File.WriteAllText(path, SpikeConsole.Snapshot());
            Console.WriteLine($"The log is saved as files/run/{Path.GetFileName(path)} in the app's private storage (spikes/Noise/README.md shows how to fetch it).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save the log: {exception.Message}");
        }
    }
}
