using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Application.Chats;

namespace P2PMessenger.Application;

public readonly record struct SendResult(SendOutcome Outcome, ChatMessage? Message)
{
    [MemberNotNullWhen(true, nameof(Message))]
    public bool IsSent => Message is not null;
}
