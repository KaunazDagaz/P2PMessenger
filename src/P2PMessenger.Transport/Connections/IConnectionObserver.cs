using P2PMessenger.Core.Messages;

namespace P2PMessenger.Transport.Connections;

public interface IConnectionObserver
{
    ValueTask ConnectedAsync(ContactConnection connection, CancellationToken cancellationToken);

    ValueTask MessageReceivedAsync(ContactConnection connection, SessionMessage message, CancellationToken cancellationToken);

    ValueTask DisconnectedAsync(ContactConnection connection, CancellationToken cancellationToken);
}
