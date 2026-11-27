using P2PMessenger.Core.Crypto;

namespace P2PMessenger.UI.ViewModels;

public sealed class GroupSuccessorOptionViewModel : ViewModelBase
{
    public GroupSuccessorOptionViewModel(Ed25519PublicKey identityKey, string name)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);

        IdentityKey = identityKey;
        Name = name;
        Fingerprint = Core.Identity.Fingerprint.Of(identityKey).ToGroupedString();
    }

    public Ed25519PublicKey IdentityKey { get; }

    public string Name { get; }

    public string Fingerprint { get; }

    public override string ToString() => $"{Name} ({Fingerprint})";
}
