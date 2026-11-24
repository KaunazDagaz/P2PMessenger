namespace P2PMessenger.Transport.Tor;

public enum TorStartFailure
{
    DataFolder,
    Process,
    ControlPort,
    Control,
    OnionService,
}
