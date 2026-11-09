using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace P2PMessenger.Spikes.Tor;

public sealed class TorProcess : IAsyncDisposable
{
    private const string ControlPortFileName = "control-port";
    private const string CookieFileName = "control_auth_cookie";
    private const string ConsensusFileName = "cached-microdesc-consensus";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] Events = ["STATUS_CLIENT", "HS_DESC"];

    private readonly Process _process;
    private readonly TorLog _log;
    private readonly IDisposable _subscription;
    private readonly TaskCompletionSource<long> _bootstrapped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long>> _uploads = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<HiddenServiceDescriptorEvent> _descriptorFailures = new();
    private int _disposed;

    private TorProcess(Process process, TorLog log, TorControlClient control, long startedTimestamp, string dataDirectory, bool hadCachedState)
    {
        _process = process;
        _log = log;
        Control = control;
        StartedTimestamp = startedTimestamp;
        DataDirectory = dataDirectory;
        HadCachedState = hadCachedState;
        _subscription = control.Subscribe(OnEvent);
    }

    public event EventHandler<BootstrapPhase>? BootstrapProgress;

    public TorControlClient Control { get; }

    public long StartedTimestamp { get; }

    public string DataDirectory { get; }

    public bool HadCachedState { get; }

    public string Version { get; private set; } = string.Empty;

    public IPEndPoint? SocksEndPoint { get; private set; }

    public BootstrapPhase? LastBootstrapPhase { get; private set; }

    public IReadOnlyCollection<HiddenServiceDescriptorEvent> DescriptorFailures => _descriptorFailures;

    public bool HasExited => _process.HasExited;

    public static async Task<TorProcess> StartAsync(TorLaunchOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dataDirectory = Path.GetFullPath(options.DataDirectory);
        CreatePrivateDirectory(dataDirectory);

        var hadCachedState = File.Exists(Path.Combine(dataDirectory, ConsensusFileName));
        var controlPortFile = Path.Combine(dataDirectory, ControlPortFileName);
        var cookieFile = Path.Combine(dataDirectory, CookieFileName);
        var torrc = Path.Combine(dataDirectory, "torrc");
        var torrcDefaults = Path.Combine(dataDirectory, "torrc-defaults");
        File.Delete(controlPortFile);
        await File.WriteAllTextAsync(torrc, string.Empty, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(torrcDefaults, string.Empty, cancellationToken).ConfigureAwait(false);

        var startInfo = CreateStartInfo(options.TorExecutable, dataDirectory, torrc, torrcDefaults, controlPortFile, cookieFile);
        var log = new TorLog(options.LogFile);
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => log.Add(e.Data);
        process.ErrorDataReceived += (_, e) => log.Add(e.Data);

        var started = Stopwatch.GetTimestamp();
        TorControlClient? control = null;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var controlEndPoint = await WaitForControlPortAsync(process, controlPortFile, log, options.StartTimeout, cancellationToken).ConfigureAwait(false);
            control = await TorControlClient.ConnectAsync(controlEndPoint, cancellationToken).ConfigureAwait(false);
            await control.AuthenticateWithCookieAsync(cookieFile, cancellationToken).ConfigureAwait(false);
            await control.TakeOwnershipAsync(cancellationToken).ConfigureAwait(false);

            var tor = new TorProcess(process, log, control, started, dataDirectory, hadCachedState);
            await tor.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return tor;
        }
        catch
        {
            if (control is not null)
            {
                await control.DisposeAsync().ConfigureAwait(false);
            }

            Kill(process);
            process.Dispose();
            log.Dispose();
            throw;
        }
    }

    public async Task<TimeSpan> WaitForBootstrapAsync(CancellationToken cancellationToken)
    {
        var finished = await _bootstrapped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Stopwatch.GetElapsedTime(StartedTimestamp, finished);
    }

    public async Task<OnionPublication> PublishOnionServiceAsync(OnionServiceKey key, int virtualPort, IPEndPoint target, CancellationToken cancellationToken)
    {
        var requested = Stopwatch.GetTimestamp();
        var serviceId = await Control.AddOnionAsync(key, virtualPort, target, cancellationToken).ConfigureAwait(false);
        var added = Stopwatch.GetTimestamp();
        var upload = _uploads.GetOrAdd(serviceId, _ => new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously));
        return new OnionPublication(serviceId, requested, added, upload.Task);
    }

    public string LogTail() => _log.Tail();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _subscription.Dispose();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Control.SignalAsync("SHUTDOWN", timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TorControlException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }

        await Control.DisposeAsync().ConfigureAwait(false);

        try
        {
            using var timeout = new CancellationTokenSource(ShutdownTimeout);
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(_process);
        }

        _process.Dispose();
        _log.Dispose();
    }

    private static ProcessStartInfo CreateStartInfo(string torExecutable, string dataDirectory, string torrc, string torrcDefaults, string controlPortFile, string cookieFile)
    {
        var startInfo = new ProcessStartInfo(torExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = dataDirectory,
        };

        string[] arguments =
        [
            "-f", torrc,
            "--defaults-torrc", torrcDefaults,
            "--DataDirectory", dataDirectory,
            "--SocksPort", "auto ExtendedErrors",
            "--ControlPort", "auto",
            "--ControlPortWriteToFile", controlPortFile,
            "--CookieAuthentication", "1",
            "--CookieAuthFile", cookieFile,
            "--__OwningControllerProcess", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--Log", "notice stdout",
        ];

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var executablePath = Path.GetFullPath(torExecutable);
        if (!File.Exists(executablePath))
        {
            return startInfo;
        }

        var executableDirectory = Path.GetDirectoryName(executablePath)!;
        var dataFolder = Path.Combine(executableDirectory, "..", "data");
        var geoIp = Path.Combine(dataFolder, "geoip");
        var geoIp6 = Path.Combine(dataFolder, "geoip6");
        if (File.Exists(geoIp) && File.Exists(geoIp6))
        {
            startInfo.ArgumentList.Add("--GeoIPFile");
            startInfo.ArgumentList.Add(Path.GetFullPath(geoIp));
            startInfo.ArgumentList.Add("--GeoIPv6File");
            startInfo.ArgumentList.Add(Path.GetFullPath(geoIp6));
        }

        if (OperatingSystem.IsLinux())
        {
            var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            startInfo.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existing) ? executableDirectory : executableDirectory + ":" + existing;
        }

        return startInfo;
    }

    private static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static async Task<IPEndPoint> WaitForControlPortAsync(Process process, string controlPortFile, TorLog log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (true)
            {
                if (process.HasExited)
                {
                    throw new TorControlException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"tor exited with code {process.ExitCode} before it opened its control port. Its last log lines:{Environment.NewLine}{log.Tail()}"));
                }

                if (File.Exists(controlPortFile))
                {
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(controlPortFile, timeoutSource.Token).ConfigureAwait(false);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("PORT=", StringComparison.Ordinal) && IPEndPoint.TryParse(line["PORT=".Length..], out var endPoint))
                            {
                                return endPoint;
                            }
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TorControlException($"tor did not open its control port within {timeout.TotalSeconds:0} s. Its last log lines:{Environment.NewLine}{log.Tail()}");
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IPEndPoint? ParseFirstEndPoint(string listeners)
    {
        foreach (var listener in ControlSyntax.ParseQuotedList(listeners))
        {
            if (IPEndPoint.TryParse(listener, out var endPoint))
            {
                return endPoint;
            }
        }

        return null;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Control.SetEventsAsync(Events, cancellationToken).ConfigureAwait(false);
        var info = await Control.GetInfoAsync(["version", "net/listeners/socks", "status/bootstrap-phase"], cancellationToken).ConfigureAwait(false);
        Version = info.GetValueOrDefault("version", string.Empty);
        SocksEndPoint = ParseFirstEndPoint(info.GetValueOrDefault("net/listeners/socks", string.Empty));
        if (TorEvents.TryParseBootstrapPhase(info.GetValueOrDefault("status/bootstrap-phase", string.Empty), out var phase))
        {
            OnBootstrapPhase(phase);
        }
    }

    private void OnEvent(TorReply reply)
    {
        var text = reply.FirstLine;
        if (text.StartsWith("STATUS_CLIENT ", StringComparison.Ordinal) && TorEvents.TryParseBootstrapPhase(text, out var phase))
        {
            OnBootstrapPhase(phase);
        }
        else if (TorEvents.TryParseHiddenServiceDescriptor(text, out var descriptorEvent))
        {
            OnDescriptorEvent(descriptorEvent);
        }
    }

    private void OnBootstrapPhase(BootstrapPhase phase)
    {
        LastBootstrapPhase = phase;
        BootstrapProgress?.Invoke(this, phase);
        if (phase.Progress >= 100)
        {
            _bootstrapped.TrySetResult(Stopwatch.GetTimestamp());
        }
    }

    private void OnDescriptorEvent(HiddenServiceDescriptorEvent descriptorEvent)
    {
        switch (descriptorEvent.Action)
        {
            case "UPLOADED":
                var now = Stopwatch.GetTimestamp();
                _uploads.GetOrAdd(descriptorEvent.Address, _ => new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(now);
                break;
            case "FAILED":
                _descriptorFailures.Enqueue(descriptorEvent);
                break;
            default:
                break;
        }
    }
}

public sealed record OnionPublication(string ServiceId, long RequestedTimestamp, long AddedTimestamp, Task<long> FirstUpload)
{
    public string Address => ServiceId + ".onion";

    public TimeSpan AddOnionDuration => Stopwatch.GetElapsedTime(RequestedTimestamp, AddedTimestamp);

    public async Task<TimeSpan> WaitForFirstUploadAsync(CancellationToken cancellationToken)
    {
        var uploaded = await FirstUpload.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Stopwatch.GetElapsedTime(AddedTimestamp, uploaded);
    }
}
