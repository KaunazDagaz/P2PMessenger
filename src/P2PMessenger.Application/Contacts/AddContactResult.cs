using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Contacts;

public readonly record struct AddContactResult(Contact? Contact, AddContactOutcome Outcome)
{
    [MemberNotNullWhen(true, nameof(Contact))]
    public bool IsSaved => Contact is not null;
}
