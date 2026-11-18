namespace P2PMessenger.Application.Connections;

public enum ListenResult
{
    Listening = 0,
    PortInUse = 1,
    Failed = 2,
    InvalidPort = 3,
}
