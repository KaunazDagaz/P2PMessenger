using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed class Pages
{
    private readonly AppServices services;
    private readonly INavigationService navigation;
    private readonly Func<CancellationToken, Task> restart;
    private readonly Func<CancellationToken, Task<bool>> deleteAll;

    public Pages(AppServices services, INavigationService navigation, Func<CancellationToken, Task> restart, Func<CancellationToken, Task<bool>> deleteAll)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(deleteAll);

        this.services = services;
        this.navigation = navigation;
        this.restart = restart;
        this.deleteAll = deleteAll;
    }

    private Messenger Messenger => services.Messenger ?? throw new InvalidOperationException("The messenger has not started yet.");

    public StorageProblemViewModel StorageProblem(LocalDataProblem problem) => new(problem, restart, new DeleteAllDataViewModel(deleteAll, UiText.DeleteAllDataIntroProblem));

    public UnlockViewModel Unlock(bool atStart, Func<string, CancellationToken, Task<bool>> unlock) =>
        new(atStart, unlock, new DeleteAllDataViewModel(deleteAll, UiText.DeleteAllDataIntroUnlock));

    public DeleteAllDataPageViewModel DeleteAllData() => new(new DeleteAllDataViewModel(deleteAll, null, true, navigation.GoBack));

    public CreateIdentityViewModel CreateIdentity() => new(services, navigation, this, StorageProblem);

    public PassphraseViewModel Passphrase() => new(services);

    public ContactListViewModel ContactList() => new(Messenger, services.UiThread, navigation, this, services.SupportsPassphrase && services.Mode == LocalDataMode.Saved);

    public ProfileViewModel Profile() => new(Messenger, services.Clipboard, services.CodeFiles);

    public AddContactViewModel AddContact() => new(Messenger, services.CodeFiles, navigation, this);

    public ConversationViewModel Conversation(Contact contact) => new(Messenger, contact.IdentityKey, services.UiThread, navigation, this);

    public VerifyContactViewModel VerifyContact(Contact contact) => new(Messenger, contact.IdentityKey, services.UiThread);

    public NetworkViewModel Network() => new(Messenger, services.UiThread);
}
