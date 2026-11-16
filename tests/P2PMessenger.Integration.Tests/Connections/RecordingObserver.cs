using System.Threading.Channels;
using P2PMessenger.Core.Messages;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Integration.Tests.Connections;

internal sealed class RecordingObserver : IConnectionObserver
{
    private readonly Channel<ContactConnection> connected = Channel.CreateUnbounded<ContactConnection>();
    private readonly Channel<ReceivedMessage> messages = Channel.CreateUnbounded<ReceivedMessage>();
    private readonly Channel<ContactConnection> disconnected = Channel.CreateUnbounded<ContactConnection>();
    private int connectedCount;
    private int messageCount;
    private int disconnectedCount;

    public int ConnectedCount => Volatile.Read(ref connectedCount);

    public int MessageCount => Volatile.Read(ref messageCount);

    public int DisconnectedCount => Volatile.Read(ref disconnectedCount);

    public int OpenCount => ConnectedCount - DisconnectedCount;

    public Func<ContactConnection, SessionMessage, Task>? OnMessage { get; set; }

    public ValueTask ConnectedAsync(ContactConnection connection, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref connectedCount);
        _ = connected.Writer.TryWrite(connection);
        return ValueTask.CompletedTask;
    }

    public async ValueTask MessageReceivedAsync(ContactConnection connection, SessionMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref messageCount);
        _ = messages.Writer.TryWrite(new ReceivedMessage(connection, message));
        if (OnMessage is { } handler)
        {
            await handler(connection, message);
        }
    }

    public ValueTask DisconnectedAsync(ContactConnection connection, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref disconnectedCount);
        _ = disconnected.Writer.TryWrite(connection);
        return ValueTask.CompletedTask;
    }

    public async Task<ContactConnection> NextConnectedAsync(CancellationToken cancellationToken) =>
        await connected.Reader.ReadAsync(cancellationToken);

    public async Task<ReceivedMessage> NextMessageAsync(CancellationToken cancellationToken) =>
        await messages.Reader.ReadAsync(cancellationToken);

    public async Task<ContactConnection> NextDisconnectedAsync(CancellationToken cancellationToken) =>
        await disconnected.Reader.ReadAsync(cancellationToken);
}

internal sealed record ReceivedMessage(ContactConnection Connection, SessionMessage Message);
