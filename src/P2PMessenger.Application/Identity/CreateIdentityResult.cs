using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Identity;

public readonly record struct CreateIdentityResult(OwnIdentity? Identity, CreateIdentityError Error)
{
    [MemberNotNullWhen(true, nameof(Identity))]
    public bool IsCreated => Identity is not null;

    internal static CreateIdentityResult Created(OwnIdentity identity) => new(identity, CreateIdentityError.None);

    internal static CreateIdentityResult Failed(CreateIdentityError error) => new(null, error);
}
