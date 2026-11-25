namespace P2PMessenger.Application.Connections;

public enum TorDialStatus
{
    None = 0,
    Connecting = 1,
    NotFound = 2,
    NoAnswer = 3,
    TimedOut = 4,
    Refused = 5,
    Failed = 6,
}
