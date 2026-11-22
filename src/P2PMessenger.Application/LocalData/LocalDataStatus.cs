namespace P2PMessenger.Application.LocalData;

public enum LocalDataStatus
{
    Opened = 0,
    NotCreated = 1,
    PassphraseNeeded = 2,
    WrongPassphrase = 3,
    PassphraseRequired = 4,
    PassphraseNotAccepted = 5,
    Failed = 6,
}
