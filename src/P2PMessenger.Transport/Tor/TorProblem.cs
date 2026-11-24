namespace P2PMessenger.Transport.Tor;

public enum TorProblem
{
    None,
    ClockWrong,
    CannotReachTor,
    StartFailed,
    Stopped,
}
