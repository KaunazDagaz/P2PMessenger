namespace P2PMessenger.Application.Groups;

public enum LeaveGroupOutcome
{
    Left = 0,
    NotAGroup = 1,
    NotActive = 2,
    SuccessorRequired = 3,
    InvalidSuccessor = 4,
}
