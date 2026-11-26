using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core.Copy;
using P2PMessenger.Transport.Connections;
using P2PMessenger.Transport.Logging;

namespace P2PMessenger.Transport.Copy;

public sealed class CopyChannel : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly CopySession session;
    private readonly CopyOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly Lock sessionGate = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly Task keepAliveLoop;
    private long lastSent;
    private int closed;
    private int disposed;

    internal CopyChannel(Stream stream, CopySession session, CopyOptions options, TimeProvider timeProvider, ILogger logger)
    {
        this.stream = stream;
        this.session = session;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
        lastSent = timeProvider.GetTimestamp();
        keepAliveLoop = KeepAliveLoopAsync(stop.Token);
    }

    public bool IsClosed => Volatile.Read(ref closed) != 0;

    public async Task<bool> SendAsync(CopyMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (IsClosed)
        {
            return false;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        linked.CancelAfter(options.WriteTimeout);
        try
        {
            await writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            byte[]? frame = EncodeFrame(message);
            if (frame is null)
            {
                return false;
            }

            await stream.WriteAsync(frame, linked.Token).ConfigureAwait(false);
            Volatile.Write(ref lastSent, timeProvider.GetTimestamp());
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Close(ConnectionCloseReason.WriteFailed);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            Close(ConnectionCloseReason.PeerClosed);
            return false;
        }
        finally
        {
            ReleaseWriteGate();
        }
    }

    public async Task<CopyMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (!IsClosed && !cancellationToken.IsCancellationRequested)
        {
            using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
            reading.CancelAfter(options.IdleTimeout);
            try
            {
                byte[] header = new byte[CopyFrame.HeaderLength];
                if (!await TryReadExactlyAsync(stream, header, reading.Token).ConfigureAwait(false))
                {
                    Close(ConnectionCloseReason.PeerClosed);
                    return null;
                }

                if (!CopyFrame.TryReadTransportLength(header, out int bodyLength))
                {
                    Close(ConnectionCloseReason.ProtocolError);
                    return null;
                }

                byte[] body = new byte[bodyLength];
                if (!await TryReadExactlyAsync(stream, body, reading.Token).ConfigureAwait(false))
                {
                    Close(ConnectionCloseReason.PeerClosed);
                    return null;
                }

                CopyMessage? message = DecodeFrame(body);
                if (message is null)
                {
                    Close(ConnectionCloseReason.ProtocolError);
                    return null;
                }

                if (message is CopyKeepAlive)
                {
                    continue;
                }

                return message;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                Close(ConnectionCloseReason.IdleTimeout);
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                Close(ConnectionCloseReason.PeerClosed);
                return null;
            }
        }

        return null;
    }

    public void Close(ConnectionCloseReason reason = ConnectionCloseReason.ClosedLocally)
    {
        if (Interlocked.Exchange(ref closed, 1) != 0)
        {
            return;
        }

        logger.CopyClosed(reason);
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lock (sessionGate)
        {
            session.Dispose();
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Close(ConnectionCloseReason.ShutDown);
        await keepAliveLoop.ConfigureAwait(false);
        stop.Dispose();
        writeGate.Dispose();
    }

    internal static async Task<bool> TryReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private byte[]? EncodeFrame(CopyMessage message)
    {
        lock (sessionGate)
        {
            if (session.IsClosed)
            {
                return null;
            }

            return session.WriteFrame(message);
        }
    }

    private CopyMessage? DecodeFrame(ReadOnlySpan<byte> body)
    {
        lock (sessionGate)
        {
            return session.TryReadFrame(body, out CopyMessage? message) ? message : null;
        }
    }

    private void ReleaseWriteGate()
    {
        try
        {
            writeGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan elapsed = timeProvider.GetElapsedTime(Volatile.Read(ref lastSent));
                TimeSpan remaining = options.KeepAliveInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await SendAsync(CopyKeepAlive.Instance, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
