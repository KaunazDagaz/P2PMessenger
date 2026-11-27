namespace P2PMessenger.Application.Groups;

public enum RemoveMemberOutcome
{
    Removed = 0,
    EndedGroup = 1,
    NotAGroup = 2,
    NotAuthorized = 3,
    NotAMember = 4,
    CannotRemoveSelf = 5,
    NotActive = 6,
}
