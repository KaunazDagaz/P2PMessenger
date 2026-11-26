using P2PMessenger.Application.Groups;

namespace P2PMessenger.UI.ViewModels;

public sealed class GroupMemberToAddViewModel : ViewModelBase
{
    public GroupMemberToAddViewModel(GroupMemberStatus member)
    {
        ArgumentNullException.ThrowIfNull(member);

        Name = member.Name.Value;
        Fingerprint = P2PMessenger.Core.Identity.Fingerprint.Of(member.IdentityKey).ToGroupedString();
    }

    public string Name { get; }

    public string Fingerprint { get; }
}
