using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Tor;

public sealed class OnionListener : IAsyncDisposable
{
    private const int Backlog = 32;

    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly ConnectionManager manager;
    private readonly Socket socket;
    private readonly ILogger logger;
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private readonly HashSet<Task> handlers = [];
    private Task acceptLoop = Task.CompletedTask;
    private int disposed;

    private OnionListener(ConnectionManager manager, Socket socket, OnionTarget target, ILogger logger)
    {
        this.manager = manager;
        this.socket = socket;
        this.logger = logger;
        Target = target;
    }

    public OnionTarget Target { get; }

    public OnionTargetKind Kind => Target.IsUnixSocket ? OnionTargetKind.UnixSocket : OnionTargetKind.LoopbackPort;

    public static OnionListener Start(ConnectionManager manager, string? socketPath, ILogger<OnionListener> logger)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(logger);

        if (CanUseUnixSocket(socketPath))
        {
            try
            {
                return Begin(manager, BindUnix(socketPath!), OnionTarget.UnixSocket(socketPath!), logger);
            }
            catch (SocketException exception)
            {
                logger.OnionSocketUnavailable(exception.SocketErrorCode);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.OnionSocketUnavailable(SocketError.AccessDenied);
            }
        }

        Socket loopback = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            loopback.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            loopback.Listen(Backlog);
        }
        catch
        {
            loopback.Dispose();
            throw;
        }

        int port = ((IPEndPoint)loopback.LocalEndPoint!).Port;
        return Begin(manager, loopback, OnionTarget.Loopback(port), logger);
    }

    public static bool CanUseUnixSocket(string? socketPath) =>
        !OperatingSystem.IsWindows() && Socket.OSSupportsUnixDomainSockets && OnionTarget.IsUsableSocketPath(socketPath);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        socket.Dispose();
        await acceptLoop.ConfigureAwait(false);
        await Task.WhenAll(SnapshotHandlers()).ConfigureAwait(false);
        stopping.Dispose();
        if (Target.SocketPath is { } path)
        {
            TryDelete(path);
        }
    }

    private static OnionListener Begin(ConnectionManager manager, Socket socket, OnionTarget target, ILogger logger)
    {
        OnionListener started = new(manager, socket, target, logger);
        logger.OnionListening(started.Kind);
        started.acceptLoop = started.AcceptLoopAsync(started.stopping.Token);
        return started;
    }

    private static Socket BindUnix(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("tor on Windows has no Unix sockets.");
        }

        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            throw new IOException("The socket path has no folder.");
        }

        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TryDelete(path);
        Socket unix = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            unix.Bind(new UnixDomainSocketEndPoint(path));
            unix.Listen(Backlog);
            return unix;
        }
        catch
        {
            unix.Dispose();
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<bool> PauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(AcceptRetryDelay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket accepted;
            try
            {
                accepted = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            catch (SocketException exception)
            {
                logger.AcceptFailed(exception.SocketErrorCode);
                if (!await PauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            Track(HandleAsync(accepted, cancellationToken));
        }
    }

    private async Task HandleAsync(Socket accepted, CancellationToken cancellationToken)
    {
        NetworkStream stream;
        try
        {
            if (accepted.ProtocolType == ProtocolType.Tcp)
            {
                accepted.NoDelay = true;
            }

            stream = new NetworkStream(accepted, ownsSocket: true);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException)
        {
            accepted.Dispose();
            return;
        }

        await manager.AcceptAsync(stream, null, cancellationToken).ConfigureAwait(false);
    }

    private void Track(Task handler)
    {
        lock (gate)
        {
            handlers.Add(handler);
        }

        _ = handler.ContinueWith(
            static (finished, state) => ((OnionListener)state!).Untrack(finished),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Untrack(Task handler)
    {
        lock (gate)
        {
            handlers.Remove(handler);
        }
    }

    private Task[] SnapshotHandlers()
    {
        lock (gate)
        {
            return [.. handlers];
        }
    }
}
