using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class NetworkContactViewModel : ViewModelBase
{
    private readonly Messenger messenger;
    private readonly Contact contact;

    [ObservableProperty]
    private string addressText;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    public NetworkContactViewModel(Messenger messenger, Contact contact)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(contact);

        this.messenger = messenger;
        this.contact = contact;
        addressText = messenger.Connections.TryGetAddress(contact.IdentityKey, out IPEndPoint? address)
            ? address.ToString()
            : string.Empty;
        Refresh();
    }

    public string Name => contact.Name.Value;

    public string Fingerprint => contact.Fingerprint.ToGroupedString();

    public void Refresh()
    {
        bool hasAddress = messenger.Connections.TryGetAddress(contact.IdentityKey, out _);
        StatusText = messenger.Connections.IsConnected(contact.IdentityKey)
            ? UiText.ContactConnected
            : DialTexts.Failure(messenger.Connections.DialStatusOf(contact.IdentityKey))
                ?? (hasAddress ? UiText.DialConnecting : UiText.DialNoAddress);
    }

    [RelayCommand]
    private void Save()
    {
        if (!IPEndPoint.TryParse(AddressText.Trim(), out IPEndPoint? address) || address.Port == 0)
        {
            ErrorMessage = UiText.AddContactAddressInvalid;
            return;
        }

        ErrorMessage = null;
        AddressText = address.ToString();
        messenger.Connections.SetAddress(contact.IdentityKey, address);
        Refresh();
    }

    [RelayCommand]
    private void Clear()
    {
        ErrorMessage = null;
        AddressText = string.Empty;
        messenger.Connections.SetAddress(contact.IdentityKey, null);
        Refresh();
    }
}
