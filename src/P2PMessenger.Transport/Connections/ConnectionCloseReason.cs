namespace P2PMessenger.Transport.Connections;

public enum ConnectionCloseReason
{
    None = 0,
    ClosedLocally = 1,
    Replaced = 2,
    PeerClosed = 3,
    ConnectionLost = 4,
    IdleTimeout = 5,
    ProtocolError = 6,
    WriteFailed = 7,
    ObserverFailed = 8,
    ShutDown = 9,
}
