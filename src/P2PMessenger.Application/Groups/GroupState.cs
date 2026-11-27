namespace P2PMessenger.Application.Groups;

public enum GroupState
{
    Joining = 0,
    WaitingForMembers = 1,
    Created = 2,
    ListsDiffer = 3,
    Left = 4,
    Removed = 5,
    Ended = 6,
}
