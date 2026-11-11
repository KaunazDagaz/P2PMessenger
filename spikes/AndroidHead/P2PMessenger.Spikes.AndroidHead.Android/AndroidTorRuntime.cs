using Android.Content;
using P2PMessenger.Spikes.Tor.Cli;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal sealed class AndroidTorRuntime(Context context, IBackgroundService background) : ITorRuntime
{
    private static readonly string[] TorFileNames = ["libTor.so", "libtor.so"];

    private readonly Lock _gate = new();
    private CancellationTokenSource? _running;
    private string? _runningName;

    public string Start(string command, ServiceRequest request)
    {
        var tor = FindTor();
        if (tor is null)
        {
            return "No tor binary in the native library folder: Android did not extract it at install.";
        }

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_running is not null)
            {
                return $"{_runningName} is still running; stop it first.";
            }

            cancellation = new CancellationTokenSource();
            _running = cancellation;
            _runningName = command;
        }

        var state = Path.Combine(context.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), "run");
        var options = new SpikeOptions(command) { TorExecutable = tor, StateDirectory = state, Name = "phone-" + command };
        var startedService = !SpikeService.IsRunning;
        if (startedService)
        {
            SpikeLog.Write(background.Start(request));
        }

        _ = Task.Run(async () =>
        {
            SpikeLog.Write($"=== tor {command} started");
            try
            {
                var exitCode = command switch
                {
                    "selftest" => await SelfTestCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                    "pair" => await PairCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                    "serve" => await ServeCommand.RunAsync(options, cancellation.Token).ConfigureAwait(false),
                    _ => 2,
                };
                SpikeLog.Write($"=== tor {command} finished with exit code {exitCode}");
            }
            catch (OperationCanceledException)
            {
                SpikeLog.Write($"=== tor {command} stopped");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                SpikeLog.Write($"=== tor {command} failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                SaveLog(state, command);
                if (startedService)
                {
                    await WaitForServiceAsync().ConfigureAwait(false);
                    SpikeLog.Write(background.Halt());
                }

                lock (_gate)
                {
                    _running = null;
                    _runningName = null;
                }

                cancellation.Dispose();
            }
        });

        return $"tor {command} is starting with {tor}";
    }

    public string Halt()
    {
        CancellationTokenSource? running;
        string? name;
        lock (_gate)
        {
            running = _running;
            name = _runningName;
        }

        if (running is null)
        {
            return "No tor command is running.";
        }

        try
        {
            _ = running.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }

        return $"Stopping tor {name}.";
    }

    private static async Task WaitForServiceAsync()
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!SpikeService.IsRunning && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        if (waited.Elapsed > TimeSpan.FromMilliseconds(100))
        {
            SpikeLog.Write($"the service needed {waited.Elapsed.TotalMilliseconds:0} ms to reach the foreground before it could be stopped safely");
        }
    }

    private static void SaveLog(string state, string command)
    {
        try
        {
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, $"console-{command}.txt"), SpikeLog.Snapshot());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SpikeLog.Write($"Could not save the log: {exception.Message}");
        }
    }

    private string? FindTor()
    {
        var folder = context.ApplicationInfo?.NativeLibraryDir;
        return folder is null
            ? null
            : TorFileNames.Select(fileName => Path.Combine(folder, fileName)).FirstOrDefault(File.Exists);
    }
}
