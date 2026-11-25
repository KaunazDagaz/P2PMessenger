using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Tcp;

public sealed class TcpConnectionListener : IAsyncDisposable
{
    private const int Backlog = 64;

    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly ConnectionManager manager;
    private readonly TcpListener listener;
    private readonly ILogger<TcpConnectionListener> logger;
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private readonly HashSet<Task> handlers = [];
    private Task acceptLoop = Task.CompletedTask;
    private int disposed;

    private TcpConnectionListener(ConnectionManager manager, TcpListener listener, ILogger<TcpConnectionListener> logger)
    {
        this.manager = manager;
        this.listener = listener;
        this.logger = logger;
        LocalEndpoint = (IPEndPoint)listener.LocalEndpoint;
    }

    public IPEndPoint LocalEndpoint { get; }

    public static TcpConnectionListener Start(ConnectionManager manager, IPEndPoint localEndpoint, ILogger<TcpConnectionListener> logger)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(localEndpoint);
        ArgumentNullException.ThrowIfNull(logger);

        TcpListener listener = new(localEndpoint);
        try
        {
            listener.Start(Backlog);
        }
        catch
        {
            listener.Dispose();
            throw;
        }

        TcpConnectionListener started = new(manager, listener, logger);
        int port = started.LocalEndpoint.Port;
        logger.Listening(port);
        started.acceptLoop = started.AcceptLoopAsync(started.stopping.Token);
        return started;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        listener.Dispose();
        await acceptLoop.ConfigureAwait(false);
        await Task.WhenAll(SnapshotHandlers()).ConfigureAwait(false);
        stopping.Dispose();
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
            Socket socket;
            try
            {
                socket = await listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
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
                SocketError error = exception.SocketErrorCode;
                logger.AcceptFailed(error);
                if (!await PauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            Track(HandleAsync(socket, cancellationToken));
        }
    }

    private async Task HandleAsync(Socket socket, CancellationToken cancellationToken)
    {
        IPAddress? source;
        NetworkStream stream;
        try
        {
            socket.NoDelay = true;
            source = (socket.RemoteEndPoint as IPEndPoint)?.Address;
            stream = new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException)
        {
            socket.Dispose();
            return;
        }

        await manager.AcceptAsync(stream, source, ConnectionRoute.Nearby, cancellationToken).ConfigureAwait(false);
    }

    private void Track(Task handler)
    {
        lock (gate)
        {
            handlers.Add(handler);
        }

        _ = handler.ContinueWith(
            static (finished, state) => ((TcpConnectionListener)state!).Untrack(finished),
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
