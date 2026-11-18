using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed class Pages
{
    private readonly AppServices services;
    private readonly INavigationService navigation;

    public Pages(AppServices services, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(navigation);

        this.services = services;
        this.navigation = navigation;
    }

    private Messenger Messenger => services.Messenger ?? throw new InvalidOperationException("The messenger has not started yet.");

    public CreateIdentityViewModel CreateIdentity() => new(services, navigation, this);

    public ContactListViewModel ContactList() => new(Messenger, services.UiThread, navigation, this);

    public ProfileViewModel Profile() => new(Messenger, services.Clipboard, services.CodeFiles);

    public AddContactViewModel AddContact() => new(Messenger, services.CodeFiles, navigation, this);

    public ConversationViewModel Conversation(Contact contact) => new(Messenger, contact.IdentityKey, services.UiThread, navigation, this);

    public VerifyContactViewModel VerifyContact(Contact contact) => new(Messenger, contact.IdentityKey, services.UiThread);

    public NetworkViewModel Network() => new(Messenger, services.UiThread);
}
