using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Groups;
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

    public StorageProblemViewModel StorageProblem(LocalDataProblem problem) =>
        Own(new StorageProblemViewModel(problem, restart, Own(new DeleteAllDataViewModel(deleteAll, UiText.DeleteAllDataIntroProblem))));

    public UnlockViewModel Unlock(bool atStart, Func<string, CancellationToken, Task<bool>> unlock) =>
        Own(new UnlockViewModel(atStart, unlock, Own(new DeleteAllDataViewModel(deleteAll, UiText.DeleteAllDataIntroUnlock))));

    public DeleteAllDataPageViewModel DeleteAllData() => Own(new DeleteAllDataPageViewModel(Own(new DeleteAllDataViewModel(deleteAll, null, true, navigation.GoBack))));

    public CreateIdentityViewModel CreateIdentity() => Own(new CreateIdentityViewModel(services, navigation, this, StorageProblem));

    public PassphraseViewModel Passphrase() => Own(new PassphraseViewModel(services));

    public ContactListViewModel ContactList() =>
        Own(new ContactListViewModel(
            Messenger,
            services.UiThread,
            navigation,
            this,
            services.SupportsPassphrase && services.Mode == LocalDataMode.Saved,
            services.Background is not null,
            services.SetTorAsync,
            services.CanSendPcCopy,
            services.CanReceivePcCopy));

    public BackgroundViewModel Background(bool firstRun) =>
        Own(new BackgroundViewModel(services.Background ?? throw new InvalidOperationException("This platform has no background service."), services.QuitAsync, navigation, firstRun));

    public ProfileViewModel Profile() => Own(new ProfileViewModel(Messenger, services.Clipboard, services.CodeFiles));

    public AddContactViewModel AddContact() => Own(new AddContactViewModel(Messenger, services.CodeFiles, navigation, this));

    public AddInPersonViewModel AddInPerson() => Own(new AddInPersonViewModel(Messenger, services.QrScanner, services.UiThread, navigation, this));

    public ConversationViewModel Conversation(Contact contact) => Own(new ConversationViewModel(Messenger, contact.IdentityKey, services.UiThread, navigation, this));

    public NewGroupViewModel NewGroup() => Own(new NewGroupViewModel(Messenger, navigation, this));

    public GroupConversationViewModel GroupConversation(GroupId groupId) => Own(new GroupConversationViewModel(Messenger, groupId, services.UiThread));

    public ReceiveCopyViewModel ReceiveCopy() => Own(new ReceiveCopyViewModel(Messenger, services.UiThread, navigation));

    public SendCopyViewModel SendCopy() => Own(new SendCopyViewModel(Messenger, services.QrScanner, services.UiThread, navigation));

    public CopiedConversationViewModel CopiedConversation(CopyOriginId origin) => Own(new CopiedConversationViewModel(Messenger, origin, services.UiThread, navigation));

    public VerifyContactViewModel VerifyContact(Contact contact) => Own(new VerifyContactViewModel(Messenger, contact.IdentityKey, services.UiThread));

    public NetworkViewModel Network() => Own(new NetworkViewModel(Messenger, services.UiThread));

    private T Own<T>(T viewModel)
        where T : ViewModelBase
    {
        viewModel.FailureLog = services.FailureLog;
        return viewModel;
    }
}
