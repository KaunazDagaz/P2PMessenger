using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.Application;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class NetworkContactViewModel : ViewModelBase
{
    private readonly Messenger messenger;
    private readonly Contact contact;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string heardText = string.Empty;

    public NetworkContactViewModel(Messenger messenger, Contact contact)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(contact);

        this.messenger = messenger;
        this.contact = contact;
        Refresh();
    }

    public string Name => contact.Name.Value;

    public string Fingerprint => contact.Fingerprint.ToGroupedString();

    public void Refresh()
    {
        ConnectionService connections = messenger.Connections;
        Reachability reachability = connections.ReachabilityOf(contact.IdentityKey);
        StatusText = reachability == Reachability.Nearby
            ? UiText.ContactReachableNearby
            : DialTexts.Failure(connections.DialStatusOf(contact.IdentityKey)) ?? ReachabilityTexts.Status(contact, reachability);
        HeardText = connections.TryGetHeardEndpoint(contact.IdentityKey, out IPEndPoint? endpoint)
            ? Formats.With(Formats.Heard, endpoint)
            : UiText.NetworkNotHeard;
    }
}
