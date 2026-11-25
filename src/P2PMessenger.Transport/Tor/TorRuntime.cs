using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core;
using P2PMessenger.Core.Identity;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Tor;

public sealed class TorRuntime : IAsyncDisposable
{
    public const string ControlPortFileName = "control-port";
    public const string CookieFileName = "control_auth_cookie";

    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ControlPortPoll = TimeSpan.FromMilliseconds(50);

    private readonly TorOptions options;
    private readonly KeySet keys;
    private readonly OnionTarget target;
    private readonly ITorProcessStarter starter;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private readonly TorHealth health;
    private readonly ITimer healthTimer;
    private readonly Channel<bool> networkChanges = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private TorStatus status = TorStatus.Starting(0);
    private TorProblem? fixedProblem;
    private IPEndPoint? socksEndpoint;
    private Task loop = Task.CompletedTask;
    private int controlled;
    private int disposed;
    private bool networkAvailable = true;

    private TorRuntime(TorOptions options, KeySet keys, OnionTarget target, ITorProcessStarter starter, ILogger logger, TimeProvider timeProvider)
    {
        this.options = options;
        this.keys = keys;
        this.target = target;
        this.starter = starter;
        this.logger = logger;
        this.timeProvider = timeProvider;
        health = new TorHealth(options.WarningGrace, options.BootstrapLimit, timeProvider);
        healthTimer = timeProvider.CreateTimer(static state => ((TorRuntime)state!).Refresh(), this, HealthInterval, HealthInterval);
    }

    public event EventHandler? StatusChanged;

    public TorStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }

    public IPEndPoint? SocksEndpoint
    {
        get
        {
            lock (gate)
            {
                return socksEndpoint;
            }
        }
    }

    internal int ControlledCount => Volatile.Read(ref controlled);

    public void NetworkChanged(bool hasAnyNetwork)
    {
        if (Volatile.Read(ref disposed) != 0 || options.DisableNetwork)
        {
            return;
        }

        networkChanges.Writer.TryWrite(hasAnyNetwork);
    }

    public static TorRuntime Start(TorOptions options, KeySet keys, OnionTarget target, ILogger<TorRuntime> logger, TimeProvider timeProvider) =>
        Start(options, keys, target, logger, timeProvider, SystemTorProcessStarter.Instance);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await healthTimer.DisposeAsync().ConfigureAwait(false);
        await stopping.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        stopping.Dispose();
    }

    internal static TorRuntime Start(TorOptions options, KeySet keys, OnionTarget target, ILogger logger, TimeProvider timeProvider, ITorProcessStarter starter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(starter);
        string? problem = options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        TorRuntime runtime = new(options, keys, target, starter, logger, timeProvider);
        runtime.loop = runtime.RunAsync(runtime.stopping.Token);
        return runtime;
    }

    internal static IReadOnlyList<string> Arguments(TorOptions options, string torrc, string defaults, string controlPortFile, string cookieFile)
    {
        List<string> arguments =
        [
            "-f", torrc,
            "--defaults-torrc", defaults,
            "--quiet",
            "--DataDirectory", options.DataFolder,
            "--SocksPort", "auto ExtendedErrors",
            "--ControlPort", "auto",
            "--ControlPortWriteToFile", controlPortFile,
            "--CookieAuthentication", "1",
            "--CookieAuthFile", cookieFile,
            "--AvoidDiskWrites", "1",
            "--__OwningControllerProcess", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        ];
        if (options.DisableNetwork)
        {
            arguments.Add("--DisableNetwork");
            arguments.Add("1");
        }

        return arguments;
    }

    internal static bool TryParseControlPort(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        endpoint = null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("PORT=", StringComparison.Ordinal)
                && IPEndPoint.TryParse(trimmed.AsSpan(5), out IPEndPoint? parsed)
                && IPAddress.IsLoopback(parsed.Address)
                && parsed.Port > 0)
            {
                endpoint = parsed;
                return true;
            }
        }

        return false;
    }

    internal static IPEndPoint? ParseSocksListener(string? value)
    {
        if (value is null || !ControlSyntax.TryParseArguments(value, out List<string>? listeners, out _))
        {
            return null;
        }

        foreach (string listener in listeners)
        {
            if (IPEndPoint.TryParse(listener, out IPEndPoint? endpoint) && IPAddress.IsLoopback(endpoint.Address) && endpoint.Port > 0)
            {
                return endpoint;
            }
        }

        return null;
    }

    private static void CreatePrivateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async Task<IPEndPoint> WaitForControlPortAsync(ITorProcess process, string controlPortFile, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                if (process.HasExited)
                {
                    throw new TorStartException(TorStartFailure.Process);
                }

                if (File.Exists(controlPortFile))
                {
                    try
                    {
                        string text = await File.ReadAllTextAsync(controlPortFile, limit.Token).ConfigureAwait(false);
                        if (TryParseControlPort(text, out IPEndPoint? endpoint))
                        {
                            return endpoint;
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                await Task.Delay(ControlPortPoll, timeProvider, limit.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TorStartException(TorStartFailure.ControlPort);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        int attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            TorProblem outcome = await RunOnceAsync(attempt, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            lock (gate)
            {
                fixedProblem = outcome;
            }

            Refresh();
            try
            {
                await Task.Delay(options.RestartDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<TorProblem> RunOnceAsync(int attempt, CancellationToken cancellationToken)
    {
        while (networkChanges.Reader.TryRead(out bool latest))
        {
            networkAvailable = latest;
        }

        lock (gate)
        {
            health.Restart();
            fixedProblem = null;
            socksEndpoint = null;
        }

        Refresh();
        logger.TorStarting(attempt);
        long started = timeProvider.GetTimestamp();
        string controlPortFile = Path.Combine(options.DataFolder, ControlPortFileName);
        string cookieFile = Path.Combine(options.DataFolder, CookieFileName);
        TorLaunch launch;
        try
        {
            launch = await PrepareAsync(controlPortFile, cookieFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.TorStartFailed(TorStartFailure.DataFolder);
            return TorProblem.StartFailed;
        }

        ITorProcess process;
        try
        {
            process = starter.Start(launch);
        }
        catch (TorControlException)
        {
            logger.TorStartFailed(TorStartFailure.Process);
            return TorProblem.StartFailed;
        }

        using (process)
        {
            TorControlClient? control = null;
            try
            {
                IPEndPoint endpoint = await WaitForControlPortAsync(process, controlPortFile, options.StartTimeout, timeProvider, cancellationToken).ConfigureAwait(false);
                control = await TorControlClient.ConnectAsync(endpoint, OnEvent, options.CommandTimeout, cancellationToken).ConfigureAwait(false);
                await control.AuthenticateAsync(cookieFile, cancellationToken).ConfigureAwait(false);
                await control.TakeOwnershipAsync(cancellationToken).ConfigureAwait(false);
                await control.SetEventsAsync(TorEvents.Subscribed, cancellationToken).ConfigureAwait(false);
                string? phase = await control.GetInfoAsync("status/bootstrap-phase", cancellationToken).ConfigureAwait(false);
                IPEndPoint? socks = ParseSocksListener(await control.GetInfoAsync("net/listeners/socks", cancellationToken).ConfigureAwait(false));
                lock (gate)
                {
                    if (phase is not null && TorEvents.TryParseBootstrapPhase(phase, out TorEvent? bootstrap))
                    {
                        health.Apply(bootstrap);
                    }

                    socksEndpoint = socks;
                    health.ExpectService(keys.OnionAddress.Label);
                }

                await AddOnionAsync(control, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref controlled);
                long milliseconds = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
                logger.TorControlled(milliseconds);
                Refresh();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopAsync(process, control).ConfigureAwait(false);
                return TorProblem.Stopped;
            }
            catch (Exception exception) when (exception is TorStartException or TorControlException or IOException or SocketException)
            {
                logger.TorStartFailed(exception is TorStartException start ? start.Failure : TorStartFailure.Control);
                await StopAsync(process, control).ConfigureAwait(false);
                return TorProblem.StartFailed;
            }

            Task exited = process.WaitForExitAsync(CancellationToken.None);
            Task stopped = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            using (CancellationTokenSource running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bool? paused = networkAvailable ? false : await ApplyNetworkAsync(control, false, false, cancellationToken).ConfigureAwait(false);
                while (paused is { } wasPaused)
                {
                    Task<bool> changed = networkChanges.Reader.WaitToReadAsync(running.Token).AsTask();
                    Task done = await Task.WhenAny(exited, control.Closed, stopped, changed).ConfigureAwait(false);
                    if (done != changed || !changed.IsCompletedSuccessfully || !networkChanges.Reader.TryRead(out bool available))
                    {
                        break;
                    }

                    networkAvailable = available;
                    paused = await ApplyNetworkAsync(control, available, wasPaused, cancellationToken).ConfigureAwait(false);
                }

                await running.CancelAsync().ConfigureAwait(false);
            }

            await StopAsync(process, control).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return TorProblem.Stopped;
            }

            int? exitCode = process.ExitCode;
            double seconds = options.RestartDelay.TotalSeconds;
            logger.TorStopped(exitCode, seconds);
            return TorProblem.Stopped;
        }
    }

    private async Task<TorLaunch> PrepareAsync(string controlPortFile, string cookieFile, CancellationToken cancellationToken)
    {
        CreatePrivateFolder(options.DataFolder);
        File.Delete(controlPortFile);
        string torrc = Path.Combine(options.DataFolder, "torrc");
        string defaults = Path.Combine(options.DataFolder, "torrc-defaults");
        await File.WriteAllTextAsync(torrc, string.Empty, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(defaults, string.Empty, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        if (OperatingSystem.IsLinux() && Path.GetDirectoryName(options.ExecutablePath) is { Length: > 0 } folder)
        {
            string? existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existing) ? folder : folder + ":" + existing;
        }

        return new TorLaunch(options.ExecutablePath, Arguments(options, torrc, defaults, controlPortFile, cookieFile), environment, options.DataFolder, controlPortFile, cookieFile);
    }

    private async Task AddOnionAsync(TorControlClient control, CancellationToken cancellationToken)
    {
        byte[] expanded = new byte[OnionServiceKey.ExpandedLength];
        string serviceId;
        try
        {
            if (!keys.TryExportOnionServiceKey(expanded))
            {
                throw new TorStartException(TorStartFailure.OnionService);
            }

            serviceId = await control.AddOnionAsync(expanded, OnionService.VirtualPort, target, cancellationToken).ConfigureAwait(false);
        }
        catch (TorControlException)
        {
            throw new TorStartException(TorStartFailure.OnionService);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expanded);
        }

        if (!string.Equals(serviceId, keys.OnionAddress.Label, StringComparison.Ordinal))
        {
            throw new TorStartException(TorStartFailure.OnionService);
        }
    }

    private async Task<bool?> ApplyNetworkAsync(TorControlClient control, bool available, bool paused, CancellationToken cancellationToken)
    {
        try
        {
            if (!available)
            {
                if (!paused)
                {
                    await control.SetNetworkDisabledAsync(true, cancellationToken).ConfigureAwait(false);
                    logger.TorNetworkPaused();
                }

                lock (gate)
                {
                    socksEndpoint = null;
                }

                StatusChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (!paused)
            {
                await control.SetNetworkDisabledAsync(true, cancellationToken).ConfigureAwait(false);
            }

            await control.SetNetworkDisabledAsync(false, cancellationToken).ConfigureAwait(false);
            IPEndPoint? socks = ParseSocksListener(await control.GetInfoAsync("net/listeners/socks", cancellationToken).ConfigureAwait(false));
            lock (gate)
            {
                socksEndpoint = socks;
            }

            logger.TorNetworkRenewed();
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return paused;
        }
        catch (Exception exception) when (exception is TorControlException or IOException or SocketException or ObjectDisposedException)
        {
            string exceptionType = Failures.TypeName(exception);
            logger.TorNetworkChangeFailed(exceptionType);
            return null;
        }
    }

    private async Task StopAsync(ITorProcess process, TorControlClient? control)
    {
        if (control is not null)
        {
            if (!process.HasExited)
            {
                try
                {
                    using CancellationTokenSource shortly = new(options.ShutdownTimeout);
                    await control.ShutdownAsync(shortly.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TorControlException or OperationCanceledException or ObjectDisposedException or IOException)
                {
                }
            }

            await control.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            using CancellationTokenSource shortly = new(options.ShutdownTimeout);
            await process.WaitForExitAsync(shortly.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
        }
    }

    private void OnEvent(ControlReply reply)
    {
        if (!TorEvents.TryParse(reply.FirstLine, out TorEvent? torEvent))
        {
            return;
        }

        lock (gate)
        {
            health.Apply(torEvent);
        }

        Refresh();
    }

    private void Refresh()
    {
        TorStatus next;
        bool changed;
        lock (gate)
        {
            next = fixedProblem is { } problem
                ? new TorStatus(TorState.Unavailable, 0, problem, false)
                : health.Evaluate();
            changed = next != status;
            status = next;
        }

        if (!changed)
        {
            return;
        }

        logger.TorStatusChanged(next.State, next.Progress, next.Problem, next.OnionServicePublished);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
