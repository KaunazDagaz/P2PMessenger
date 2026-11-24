using System.Diagnostics;
using System.Globalization;
using Android.Content;
using Android.OS;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace P2PMessenger.Android.Background;

internal sealed class ServiceDiagnostics : IDisposable
{
    public const string FolderName = "diagnostics";

    private readonly MessengerService service;
    private readonly StreamWriter writer;
    private readonly Lock sync = new();
    private readonly Listener listener;
    private readonly CancellationTokenSource stopping = new();
    private bool disposed;

    private ServiceDiagnostics(MessengerService service, StreamWriter writer)
    {
        this.service = service;
        this.writer = writer;
        listener = new Listener(this);
        _ = DiagnosticsTrace.Listeners.Add(listener);
        _ = Task.Run(() => HeartbeatAsync(stopping.Token));
    }

    public static ServiceDiagnostics? Open(MessengerService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            string folder = Path.Combine(service.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no files folder."), FolderName);
            _ = Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"service-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"));
            StreamWriter writer = new(path, append: true) { AutoFlush = true };
            return new ServiceDiagnostics(service, writer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Write(string line)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {line}"));
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DiagnosticsTrace.Listeners.Remove(listener);
            stopping.Cancel();
            stopping.Dispose();
            listener.Dispose();
            writer.Dispose();
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        int number = 0;
        DateTimeOffset previousWall = DateTimeOffset.UtcNow;
        long previousAsleep = SystemClock.ElapsedRealtime() - SystemClock.UptimeMillis();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BackgroundRules.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            number++;
            DateTimeOffset wall = DateTimeOffset.UtcNow;
            long asleep = SystemClock.ElapsedRealtime() - SystemClock.UptimeMillis();
            HeartbeatReading reading = Read(number, wall - previousWall - BackgroundRules.HeartbeatInterval, TimeSpan.FromMilliseconds(asleep - previousAsleep));
            previousWall = wall;
            previousAsleep = asleep;
            Write(BackgroundRules.Heartbeat(reading));
        }
    }

    private HeartbeatReading Read(int number, TimeSpan lateness, TimeSpan asleep)
    {
        PowerManager? power = service.GetSystemService(Context.PowerService) as PowerManager;
        BatteryManager? battery = service.GetSystemService(Context.BatteryService) as BatteryManager;
        return new HeartbeatReading(
            number,
            lateness,
            asleep,
            power?.IsDeviceIdleMode == true,
            power?.IsInteractive == true,
            power?.IsPowerSaveMode == true,
            power?.IsIgnoringBatteryOptimizations(service.PackageName) == true,
            AndroidBatteryExemption.IsBackgroundRestricted(service),
            battery?.GetIntProperty((int)BatteryProperty.Capacity) ?? -1,
            battery?.IsCharging == true);
    }

    private sealed class Listener : TraceListener
    {
        private readonly ServiceDiagnostics owner;

        public Listener(ServiceDiagnostics owner)
        {
            this.owner = owner;
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                owner.Write(message);
            }
        }
    }
}
