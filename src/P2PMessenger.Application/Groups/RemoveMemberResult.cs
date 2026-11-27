using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Groups;

public readonly record struct RemoveMemberResult(RemoveMemberOutcome Outcome, Group? Group)
{
    [MemberNotNullWhen(true, nameof(Group))]
    public bool Succeeded => Outcome is RemoveMemberOutcome.Removed or RemoveMemberOutcome.EndedGroup;
}
