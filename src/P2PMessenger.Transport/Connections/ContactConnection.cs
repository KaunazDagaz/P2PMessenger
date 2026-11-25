using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading.Channels;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Connections;

public sealed class ContactConnection : IDisposable
{
    private const int OutgoingCapacity = 256;

    private readonly ConnectionManager manager;
    private readonly Stream stream;
    private readonly SecureSession session;
    private readonly PeriodicTimer keepAliveTimer;
    private readonly Lock sessionGate = new();
    private readonly Channel<OutgoingFrame> outgoing;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SessionMessage? firstMessage;
    private int closeReason;
    private int framesSinceTick;

    internal ContactConnection(long number, Stream stream, SecureSession session, SessionMessage? firstMessage, ConnectionRoute route, ConnectionManager manager)
    {
        Number = number;
        Route = route;
        this.stream = stream;
        this.session = session;
        this.firstMessage = firstMessage;
        this.manager = manager;
        Contact = session.Peer;
        IsInitiator = session.IsInitiator;
        EstablishedAt = manager.TimeProvider.GetTimestamp();
        keepAliveTimer = new PeriodicTimer(manager.Options.KeepAliveInterval, manager.TimeProvider);
        outgoing = Channel.CreateBounded<OutgoingFrame>(new BoundedChannelOptions(OutgoingCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public SessionContact Contact { get; }

    public bool IsInitiator { get; }

    public ConnectionRoute Route { get; }

    public bool IsClosed => Volatile.Read(ref closeReason) != (int)ConnectionCloseReason.None;

    public ConnectionCloseReason CloseReason => (ConnectionCloseReason)Volatile.Read(ref closeReason);

    public Task Completion => completion.Task;

    internal long Number { get; }

    internal long EstablishedAt { get; }

    public async Task<bool> SendAsync(SessionMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        TaskCompletionSource<bool> sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        OutgoingFrame frame = new(message, sent);
        ChannelWriter<OutgoingFrame> writer = outgoing.Writer;
        while (await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            if (writer.TryWrite(frame))
            {
                return await sent.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    public void Close() => Close(ConnectionCloseReason.ClosedLocally);

    public void Dispose() => Close(ConnectionCloseReason.ClosedLocally);

    internal void Close(ConnectionCloseReason reason)
    {
        if (Interlocked.CompareExchange(ref closeReason, (int)reason, (int)ConnectionCloseReason.None) != (int)ConnectionCloseReason.None)
        {
            return;
        }

        outgoing.Writer.TryComplete();
        keepAliveTimer.Dispose();
        stream.Dispose();
        lock (sessionGate)
        {
            session.Dispose();
        }
    }

    internal void Start() => _ = RunAsync();

    internal void Abandon(ConnectionCloseReason reason)
    {
        Close(reason);
        completion.TrySetResult();
    }

    private async Task RunAsync()
    {
        CancellationToken shutdown = manager.ShutdownToken;
        IConnectionObserver observer = manager.Observer;
        Task writing = WriteLoopAsync();
        Task keepingAlive = KeepAliveLoopAsync();
        bool announced = false;
        try
        {
            if (!IsClosed)
            {
                announced = true;
                await observer.ConnectedAsync(this, shutdown).ConfigureAwait(false);
                await ReadLoopAsync(observer, shutdown).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Close(ConnectionCloseReason.ShutDown);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportObserverFailure(exception);
            Close(ConnectionCloseReason.ObserverFailed);
        }

        Close(ConnectionCloseReason.ClosedLocally);
        await writing.ConfigureAwait(false);
        await keepingAlive.ConfigureAwait(false);
        manager.Detach(this);

        long number = Number;
        ConnectionCloseReason reason = CloseReason;
        manager.Logger.Closed(number, reason);
        if (announced)
        {
            try
            {
                await observer.DisconnectedAsync(this, shutdown).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportObserverFailure(exception);
            }
        }

        completion.TrySetResult();
    }

    private async Task ReadLoopAsync(IConnectionObserver observer, CancellationToken shutdown)
    {
        SessionMessage? first = firstMessage;
        firstMessage = null;
        if (first is not null and not KeepAlive)
        {
            await observer.MessageReceivedAsync(this, first, shutdown).ConfigureAwait(false);
        }

        byte[] header = new byte[Frame.HeaderLength];
        while (!IsClosed)
        {
            SessionMessage? message = await ReadMessageAsync(header).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }

            if (message is not KeepAlive)
            {
                await observer.MessageReceivedAsync(this, message, shutdown).ConfigureAwait(false);
            }
        }
    }

    private async Task<SessionMessage?> ReadMessageAsync(byte[] header)
    {
        using CancellationTokenSource idle = new(manager.Options.IdleTimeout, manager.TimeProvider);
        try
        {
            await stream.ReadExactlyAsync(header, idle.Token).ConfigureAwait(false);
            if (!Frame.TryReadTransportLength(header, out int length))
            {
                Close(ConnectionCloseReason.ProtocolError);
                return null;
            }

            byte[] body = new byte[length];
            await stream.ReadExactlyAsync(body, idle.Token).ConfigureAwait(false);
            if (TryOpen(body, out SessionMessage? message))
            {
                return message;
            }

            Close(ConnectionCloseReason.ProtocolError);
            return null;
        }
        catch (OperationCanceledException)
        {
            Close(ConnectionCloseReason.IdleTimeout);
            return null;
        }
        catch (EndOfStreamException)
        {
            Close(ConnectionCloseReason.PeerClosed);
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
        {
            Close(ConnectionCloseReason.ConnectionLost);
            return null;
        }
    }

    private async Task WriteLoopAsync()
    {
        ChannelReader<OutgoingFrame> reader = outgoing.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out OutgoingFrame frame))
                {
                    bool written = !IsClosed && await TryWriteAsync(frame.Message).ConfigureAwait(false);
                    frame.Sent?.TrySetResult(written);
                    if (!written)
                    {
                        Close(ConnectionCloseReason.WriteFailed);
                    }
                    else if (frame.Sent is not null)
                    {
                        Interlocked.Increment(ref framesSinceTick);
                    }
                }
            }
        }
        finally
        {
            while (reader.TryRead(out OutgoingFrame left))
            {
                left.Sent?.TrySetResult(false);
            }
        }
    }

    private async Task KeepAliveLoopAsync()
    {
        while (await keepAliveTimer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref framesSinceTick, 0) == 0)
            {
                _ = outgoing.Writer.TryWrite(new OutgoingFrame(KeepAlive.Instance, null));
            }
        }
    }

    private async Task<bool> TryWriteAsync(SessionMessage message)
    {
        byte[]? frame = Seal(message);
        if (frame is null)
        {
            return false;
        }

        using CancellationTokenSource timeout = new(manager.Options.WriteTimeout, manager.TimeProvider);
        try
        {
            await stream.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            return false;
        }
    }

    private byte[]? Seal(SessionMessage message)
    {
        lock (sessionGate)
        {
            return session.IsClosed ? null : session.WriteFrame(message);
        }
    }

    private bool TryOpen(byte[] body, [NotNullWhen(true)] out SessionMessage? message)
    {
        lock (sessionGate)
        {
            if (session.IsClosed)
            {
                message = null;
                return false;
            }

            return session.TryReadFrame(body, out message);
        }
    }

    private void ReportObserverFailure(Exception exception)
    {
        long number = Number;
        string exceptionType = exception.GetType().Name;
        manager.Logger.ObserverFailed(number, exceptionType);
    }

    private readonly record struct OutgoingFrame(SessionMessage Message, TaskCompletionSource<bool>? Sent);
}
