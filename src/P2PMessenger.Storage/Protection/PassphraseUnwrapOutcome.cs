namespace P2PMessenger.Storage.Protection;

public enum PassphraseUnwrapOutcome
{
    Succeeded = 0,
    WrongPassphrase = 1,
    Damaged = 2,
}
