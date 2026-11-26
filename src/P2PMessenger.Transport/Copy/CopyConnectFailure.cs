namespace P2PMessenger.Transport.Copy;

public enum CopyConnectFailure
{
    None = 0,
    Unreachable = 1,
    TimedOut = 2,
    Refused = 3,
    Cancelled = 4,
}
