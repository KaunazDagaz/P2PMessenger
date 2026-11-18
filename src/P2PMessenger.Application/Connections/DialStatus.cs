namespace P2PMessenger.Application.Connections;

public enum DialStatus
{
    None = 0,
    Connecting = 1,
    Unreachable = 2,
    TimedOut = 3,
    Refused = 4,
}
