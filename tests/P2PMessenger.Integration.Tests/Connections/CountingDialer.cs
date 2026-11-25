using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Integration.Tests.Connections;

internal sealed class CountingDialer(IDialer inner, int failuresFirst = 0) : IDialer
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public ConnectionRoute Route => inner.Route;

    public ValueTask<Stream?> DialAsync(CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref calls);
        return call <= failuresFirst ? ValueTask.FromResult<Stream?>(null) : inner.DialAsync(cancellationToken);
    }
}
