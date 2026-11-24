namespace P2PMessenger.Transport.Tor;

internal sealed class TorStartException : Exception
{
    public TorStartException(TorStartFailure failure)
        : base("tor could not be started or controlled.")
    {
        Failure = failure;
    }

    public TorStartFailure Failure { get; }
}
