namespace P2PMessenger.Spikes.Tor;

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

    public TorControlException(string message, int status)
        : base(message)
    {
        Status = status;
    }

    public int? Status { get; }
}
