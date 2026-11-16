namespace P2PMessenger.Transport.Connections;

public interface IDialer
{
    ValueTask<Stream?> DialAsync(CancellationToken cancellationToken);
}
