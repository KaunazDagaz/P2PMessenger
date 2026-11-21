using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;
using P2PMessenger.Integration.Tests.Connections;

namespace P2PMessenger.Integration.Tests.AppSlice;

internal sealed class RawContact : IAsyncDisposable
{
    private readonly RawSession raw;
    private readonly Lock gate = new();
    private readonly Channel<SessionMessage> received = Channel.CreateUnbounded<SessionMessage>();
    private readonly Task reading;

    private RawContact(RawSession raw)
    {
        this.raw = raw;
        reading = Task.Run(ReadAsync);
    }

    public static async Task<RawContact> AcceptAsync(RawServer server, KeySet own, SessionContact caller, CancellationToken cancellationToken) =>
        new(await RawSession.AcceptAsync(server, own, caller, cancellationToken));

    public static async Task<RawContact> OpenAsync(IPEndPoint endpoint, KeySet own, SessionContact responder, CancellationToken cancellationToken) =>
        new(await RawSession.OpenAsync(endpoint, own, responder, cancellationToken));

    public async Task SendAsync(SessionMessage message, CancellationToken cancellationToken)
    {
        byte[] frame;
        lock (gate)
        {
            frame = raw.Session.WriteFrame(message);
        }

        await raw.SendAsync(frame, cancellationToken);
    }

    public async Task<T> NextAsync<T>(CancellationToken cancellationToken)
        where T : SessionMessage
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            var message = await received.Reader.ReadAsync(timeout.Token);
            if (message is T wanted)
            {
                return wanted;
            }
        }
    }

    public async Task<SessionMessage?> NextWithinAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            return await received.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            raw.Dispose();
        }

        await reading;
    }

    private async Task ReadAsync()
    {
        try
        {
            var header = new byte[Frame.HeaderLength];
            while (true)
            {
                await raw.Stream.ReadExactlyAsync(header);
                if (!Frame.TryReadTransportLength(header, out var length))
                {
                    return;
                }

                var body = new byte[length];
                await raw.Stream.ReadExactlyAsync(body);
                SessionMessage? message = null;
                bool opened;
                lock (gate)
                {
                    opened = !raw.Session.IsClosed && raw.Session.TryReadFrame(body, out message);
                }

                if (!opened || message is null)
                {
                    return;
                }

                if (message is not KeepAlive)
                {
                    _ = received.Writer.TryWrite(message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            return;
        }
        finally
        {
            _ = received.Writer.TryComplete();
        }
    }
}
