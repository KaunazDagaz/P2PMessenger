using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Groups;

public sealed record GroupMember
{
    public GroupMember(Ed25519PublicKey identityKey, DisplayName name)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);

        IdentityKey = identityKey;
        Name = name;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public DisplayName Name { get; }
}
