using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Groups;

public readonly record struct CreateGroupResult(CreateGroupOutcome Outcome, Group? Group)
{
    [MemberNotNullWhen(true, nameof(Group))]
    public bool IsCreated => Group is not null;
}
