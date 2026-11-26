using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Groups;

public readonly record struct GroupSendResult(GroupSendOutcome Outcome, GroupChatMessage? Message)
{
    [MemberNotNullWhen(true, nameof(Message))]
    public bool IsQueued => Message is not null;
}
