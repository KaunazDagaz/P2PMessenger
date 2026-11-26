using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Crypto;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupDeliveryMemberViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private string name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(SummaryText))]
    private bool isReceived;

    public GroupDeliveryMemberViewModel(Ed25519PublicKey identityKey, string name, DeliveryState state)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);

        IdentityKey = identityKey;
        this.name = name;
        isReceived = state == DeliveryState.Received;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public string StatusText => IsReceived ? UiText.GroupDeliveryMemberReceived : UiText.GroupDeliveryMemberWaiting;

    public string SummaryText => Formats.WithAll(Formats.GroupDeliveryMember, Name, StatusText);
}
