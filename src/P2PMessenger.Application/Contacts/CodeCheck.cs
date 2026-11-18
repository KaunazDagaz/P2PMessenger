using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Contacts;

namespace P2PMessenger.Application.Contacts;

public sealed class CodeCheck
{
    private CodeCheck(CodeCheckOutcome outcome, ContactCode? code, Contact? existing)
    {
        Outcome = outcome;
        Code = code;
        Existing = existing;
    }

    public CodeCheckOutcome Outcome { get; }

    public ContactCode? Code { get; }

    public Contact? Existing { get; }

    [MemberNotNullWhen(true, nameof(Code))]
    public bool CanAdd => Code is not null && Outcome is CodeCheckOutcome.New or CodeCheckOutcome.NewConnectionKeys or CodeCheckOutcome.LooksLikeContact;

    internal static CodeCheck Unreadable(CodeCheckOutcome outcome) => new(outcome, null, null);

    internal static CodeCheck Of(CodeCheckOutcome outcome, ContactCode code, Contact? existing) => new(outcome, code, existing);
}
