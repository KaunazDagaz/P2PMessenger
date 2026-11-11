using System.Globalization;
using System.Net;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using P2PMessenger.Spikes.AndroidHead.Probe;

namespace P2PMessenger.Spikes.AndroidHead.Head;

[Register("io.github.kaunazdagaz.androidheadspike.SpikeService")]
public sealed class SpikeService : Service
{
    public const string ActionQuit = "io.github.kaunazdagaz.androidheadspike.QUIT";
    public const string ExtraKind = "kind";
    public const string ExtraWakeLock = "wakelock";
    public const string ExtraPort = "port";

    private const int NotificationId = 1;
    private const string ChannelId = "androidhead-spike";

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    private readonly Lock _fileGate = new();
    private ProbeListener? _listener;
    private PowerManager.WakeLock? _wakeLock;
    private DeviceEvents? _events;
    private CancellationTokenSource? _heartbeat;
    private StreamWriter? _file;
    private ServiceKind _kind;
    private int _port;

    public static bool IsRunning { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionQuit)
        {
            Quit();
            return StartCommandResult.NotSticky;
        }

        if (IsRunning)
        {
            SpikeLog.Write("service: already running; the start request is ignored");
            return StartCommandResult.NotSticky;
        }

        _kind = Enum.TryParse<ServiceKind>(intent?.GetStringExtra(ExtraKind), out var kind) ? kind : ServiceKind.ConnectedDevice;
        _port = intent?.GetIntExtra(ExtraPort, ProbeProtocol.DefaultPort) ?? ProbeProtocol.DefaultPort;
        var wakeLock = intent?.GetBooleanExtra(ExtraWakeLock, false) ?? false;

        try
        {
            PromoteToForeground();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SpikeLog.Write($"service: StartForeground as {_kind} failed: {exception.GetType().FullName}: {exception.Message}");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        IsRunning = true;
        OpenLogFile();
        if (wakeLock)
        {
            AcquireWakeLock();
        }

        StartListener();
        _events = new DeviceEvents(this);
        _heartbeat = new CancellationTokenSource();
        _ = Task.Run(() => HeartbeatAsync(_heartbeat.Token));
        SpikeLog.Write($"service: running as {_kind}; {DeviceState.Describe(this, _kind, _wakeLock?.IsHeld == true)}");
        return StartCommandResult.NotSticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        SpikeLog.Write("service: the task was swiped away from the recent apps; the service keeps running");
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        SpikeLog.Write("service: stopping");
        _heartbeat?.Cancel();
        _heartbeat?.Dispose();
        _heartbeat = null;
        _events?.Dispose();
        _events = null;
        if (_listener is not null)
        {
            var listener = _listener;
            _listener = null;
            _ = Task.Run(async () => await listener.DisposeAsync().ConfigureAwait(false));
        }

        if (_wakeLock?.IsHeld == true)
        {
            _wakeLock.Release();
        }

        _wakeLock?.Dispose();
        _wakeLock = null;
        CloseLogFile();
        IsRunning = false;
        base.OnDestroy();
    }

    private void OpenLogFile()
    {
        try
        {
            var folder = Path.Combine(FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), "run");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"service-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
            _file = new StreamWriter(path, append: true) { AutoFlush = true };
            _file.WriteLine(SpikeLog.Snapshot());
            SpikeLog.LineWritten += OnLineWritten;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SpikeLog.Write($"service: the log file could not be opened: {exception.Message}");
        }
    }

    private void CloseLogFile()
    {
        SpikeLog.LineWritten -= OnLineWritten;
        lock (_fileGate)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    private void OnLineWritten(object? sender, string line)
    {
        lock (_fileGate)
        {
            _file?.WriteLine(line);
        }
    }

    private void PromoteToForeground()
    {
        var notification = BuildNotification();
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || _kind == ServiceKind.Untyped)
        {
            StartForeground(NotificationId, notification);
        }
        else if (_kind == ServiceKind.ConnectedDevice)
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        var applied = OperatingSystem.IsAndroidVersionAtLeast(29) ? ForegroundServiceType.ToString() : "not reported before API 29";
        SpikeLog.Write($"service: StartForeground accepted ({_kind}); the system reports the type as {applied}");
    }

    private Notification BuildNotification()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        using (var channel = new NotificationChannel(ChannelId, "Android head spike", NotificationImportance.Low))
        {
            manager?.CreateNotificationChannel(channel);
        }

        using var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var quitIntent = new Intent(this, typeof(SpikeService)).SetAction(ActionQuit);
        using var quit = PendingIntent.GetService(this, 1, quitIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var icon = Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcMenuCloseClearCancel);
        using var quitAction = new Notification.Action.Builder(icon, "Quit", quit).Build();

        using var builder = new Notification.Builder(this, ChannelId);
        builder.SetContentTitle("Android head spike");
        builder.SetContentText(string.Create(CultureInfo.InvariantCulture, $"Listening on port {_port}, service type {_kind}"));
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetContentIntent(open);
        builder.AddAction(quitAction);
        return builder.Build() ?? throw new InvalidOperationException("Android returned no notification.");
    }

    private void AcquireWakeLock()
    {
        var power = (PowerManager?)GetSystemService(PowerService);
        _wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, "androidhead:spike");
        _wakeLock?.SetReferenceCounted(false);
        _wakeLock?.Acquire();
        SpikeLog.Write($"service: partial wake lock {(_wakeLock?.IsHeld == true ? "held" : "not acquired")}");
    }

    private void StartListener()
    {
        try
        {
            _listener = new ProbeListener(new IPEndPoint(IPAddress.Any, _port), () => DeviceState.Describe(this, _kind, _wakeLock?.IsHeld == true));
            _listener.Start();
            SpikeLog.Write($"service: TCP listener on port {_port}; addresses: {DeviceState.Addresses()}");
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            SpikeLog.Write($"service: the listener could not start: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        var tick = 0;
        var previousWall = DateTimeOffset.UtcNow;
        var previousAsleep = SystemClock.ElapsedRealtime() - SystemClock.UptimeMillis();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            tick++;
            var wall = DateTimeOffset.UtcNow;
            var asleep = SystemClock.ElapsedRealtime() - SystemClock.UptimeMillis();
            var lateness = wall - previousWall - HeartbeatInterval;
            var slept = TimeSpan.FromMilliseconds(asleep - previousAsleep);
            previousWall = wall;
            previousAsleep = asleep;
            SpikeLog.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"heartbeat #{tick}: timer {lateness.TotalSeconds:+0.0;-0.0;0.0} s against its due time, the processor slept {slept.TotalSeconds:0.0} s of the last interval, probes answered {_listener?.Answered ?? 0}; {DeviceState.Describe(this, _kind, _wakeLock?.IsHeld == true)}"));
        }
    }

    private void Quit()
    {
        SpikeLog.Write("service: quit requested from the notification");
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        ActivityHost.Current?.FinishAndRemoveTask();
        _ = Task.Run(async () =>
        {
            await Task.Delay(700).ConfigureAwait(false);
            Process.KillProcess(Process.MyPid());
        });
    }
}
