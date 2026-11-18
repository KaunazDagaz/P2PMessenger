namespace P2PMessenger.Application.Contacts;

public enum CodeCheckOutcome
{
    New = 0,
    NewConnectionKeys = 1,
    LooksLikeContact = 2,
    AlreadyContact = 3,
    OwnCode = 4,
    KeysInUse = 5,
    NotACode = 6,
    UnsupportedVersion = 7,
    Damaged = 8,
}
