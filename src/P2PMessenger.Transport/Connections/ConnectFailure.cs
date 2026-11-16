namespace P2PMessenger.Transport.Connections;

public enum ConnectFailure
{
    None = 0,
    Unreachable = 1,
    TimedOut = 2,
    Refused = 3,
    ShutDown = 4,
}
