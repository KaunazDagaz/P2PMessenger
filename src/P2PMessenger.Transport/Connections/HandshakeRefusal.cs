namespace P2PMessenger.Transport.Connections;

internal enum HandshakeRefusal
{
    None = 0,
    Stranger = 1,
    HandshakeFailed = 2,
    NotConfirmed = 3,
    TimedOut = 4,
    Closed = 5,
    HintTimedOut = 6,
}
