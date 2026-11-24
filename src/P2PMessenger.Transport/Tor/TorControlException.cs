namespace P2PMessenger.Transport.Tor;

public sealed class TorControlException : Exception
{
    public TorControlException()
    {
    }

    public TorControlException(string message)
        : base(message)
    {
    }

    public TorControlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
