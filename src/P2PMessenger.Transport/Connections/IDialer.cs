namespace P2PMessenger.Transport.Connections;

public interface IDialer
{
    ConnectionRoute Route { get; }

    ValueTask<Stream?> DialAsync(CancellationToken cancellationToken);
}
