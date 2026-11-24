namespace P2PMessenger.Application.Internet;

public enum InternetRouteProblem
{
    None,
    NotInBuild,
    NoNetwork,
    ClockWrong,
    CannotReachTor,
    TorFailed,
    TorStopped,
}
