namespace P2PMessenger.Application.LocalData;

public enum PassphraseChangeOutcome
{
    Changed = 0,
    WrongPassphrase = 1,
    NotAccepted = 2,
    KeyStoreFailed = 3,
    NotAvailable = 4,
}
