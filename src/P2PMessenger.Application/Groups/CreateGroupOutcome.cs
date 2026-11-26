namespace P2PMessenger.Application.Groups;

public enum CreateGroupOutcome
{
    Created = 0,
    NameNotValid = 1,
    NoMembers = 2,
    TooManyMembers = 3,
    NotAnAddedContact = 4,
}
