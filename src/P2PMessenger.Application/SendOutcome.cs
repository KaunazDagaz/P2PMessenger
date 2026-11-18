namespace P2PMessenger.Application;

public enum SendOutcome
{
    Sent = 0,
    Empty = 1,
    TooLong = 2,
    NotSendable = 3,
    NotAContact = 4,
    WaitingForFirstConnection = 5,
    NotConnected = 6,
}
