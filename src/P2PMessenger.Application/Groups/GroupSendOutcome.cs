namespace P2PMessenger.Application.Groups;

public enum GroupSendOutcome
{
    Queued = 0,
    Empty = 1,
    TooLong = 2,
    NotSendable = 3,
    NotAGroup = 4,
    NotCreatedYet = 5,
    Left = 6,
    Removed = 7,
    Ended = 8,
}
