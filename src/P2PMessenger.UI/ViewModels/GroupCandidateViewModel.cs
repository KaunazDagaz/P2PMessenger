using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupCandidateViewModel : ViewModelBase
{
    private readonly Action changed;

    [ObservableProperty]
    private bool isSelected;

    public GroupCandidateViewModel(Contact contact, Action changed)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(changed);

        IdentityKey = contact.IdentityKey;
        Name = contact.Name.Value;
        Fingerprint = contact.Fingerprint.ToGroupedString();
        this.changed = changed;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public string Name { get; }

    public string Fingerprint { get; }

    partial void OnIsSelectedChanged(bool value) => changed();
}
