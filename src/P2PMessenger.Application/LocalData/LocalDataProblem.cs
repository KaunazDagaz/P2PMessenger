namespace P2PMessenger.Application.LocalData;

public enum LocalDataProblem
{
    None = 0,
    KeyStoreUnavailable = 1,
    KeyStoreRefused = 2,
    KeyNotFound = 3,
    NewerVersion = 4,
    Damaged = 5,
}
