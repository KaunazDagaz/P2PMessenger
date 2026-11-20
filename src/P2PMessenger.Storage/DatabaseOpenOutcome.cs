namespace P2PMessenger.Storage;

public enum DatabaseOpenOutcome
{
    Opened = 0,
    NewerThanApp = 1,
    NotADatabase = 2,
}
