using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Copy;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ContactListViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private readonly Pages pages;
    private readonly Func<bool, CancellationToken, Task>? setTor;
    private readonly TimeZoneInfo timeZone;
    private object? selection;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool hasContacts;

    [ObservableProperty]
    private bool hasSearchableItems;

    [ObservableProperty]
    private bool hasNoMatches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool hasNoNetwork;

    [ObservableProperty]
    private string internetRouteText = string.Empty;

    [ObservableProperty]
    private bool canSwitchTor;

    [ObservableProperty]
    private string torSwitchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchTorCommand))]
    private bool isSwitchingTor;

    [ObservableProperty]
    private bool isMenuOpen;

    public ContactListViewModel(Messenger messenger, IUiThread uiThread, INavigationService navigation, Pages pages, bool offersPassphrase)
        : this(messenger, uiThread, navigation, pages, offersPassphrase, false, null, false, false)
    {
    }

    public ContactListViewModel(Messenger messenger, IUiThread uiThread, INavigationService navigation, Pages pages, bool offersPassphrase, bool offersBackground)
        : this(messenger, uiThread, navigation, pages, offersPassphrase, offersBackground, null, false, false)
    {
    }

    public ContactListViewModel(Messenger messenger, IUiThread uiThread, INavigationService navigation, Pages pages, bool offersPassphrase, bool offersBackground, Func<bool, CancellationToken, Task>? setTor)
        : this(messenger, uiThread, navigation, pages, offersPassphrase, offersBackground, setTor, false, false)
    {
    }

    public ContactListViewModel(
        Messenger messenger,
        IUiThread uiThread,
        INavigationService navigation,
        Pages pages,
        bool offersPassphrase,
        bool offersBackground,
        Func<bool, CancellationToken, Task>? setTor,
        bool offersSendPcCopy,
        bool offersReceivePcCopy)
        : this(messenger, uiThread, navigation, pages, offersPassphrase, offersBackground, setTor, offersSendPcCopy, offersReceivePcCopy, TimeZoneInfo.Local)
    {
    }

    public ContactListViewModel(
        Messenger messenger,
        IUiThread uiThread,
        INavigationService navigation,
        Pages pages,
        bool offersPassphrase,
        bool offersBackground,
        Func<bool, CancellationToken, Task>? setTor,
        bool offersSendPcCopy,
        bool offersReceivePcCopy,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(timeZone);

        this.messenger = messenger;
        this.uiThread = uiThread;
        this.navigation = navigation;
        this.pages = pages;
        this.setTor = setTor;
        this.timeZone = timeZone;
        OffersPassphrase = offersPassphrase;
        OffersBackground = offersBackground;
        OffersSendPcCopy = offersSendPcCopy;
        OffersReceivePcCopy = offersReceivePcCopy;
        DeleteAll = pages.DeleteAllData();
        Refresh();
    }

    public override string Title => UiText.ContactListTitle;

    public DeleteAllDataViewModel DeleteAll { get; }

    public bool OffersPassphrase { get; }

    public bool OffersBackground { get; }

    public bool OffersSendPcCopy { get; }

    public bool OffersReceivePcCopy { get; }

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public ObservableCollection<GroupItemViewModel> Groups { get; } = [];

    public ObservableCollection<IChatItem> Chats { get; } = [];

    public ObservableCollection<CopiedChatItemViewModel> CopiedChats { get; } = [];

    public bool HasCopiedChats => CopiedChats.Count > 0;

    public override void OnShown()
    {
        messenger.Contacts.Changed += OnContactChanged;
        messenger.Chats.Changed += OnChatChanged;
        messenger.Connections.Changed += OnConnectionChanged;
        messenger.InternetRoute.Changed += OnInternetRouteChanged;
        messenger.Groups.Changed += OnGroupChanged;
        messenger.GroupChats.Changed += OnGroupChatChanged;
        messenger.CopiedChats.Changed += OnCopiedChatChanged;
        Refresh();
    }

    public override void OnHidden()
    {
        IsMenuOpen = false;
        messenger.Contacts.Changed -= OnContactChanged;
        messenger.Chats.Changed -= OnChatChanged;
        messenger.Connections.Changed -= OnConnectionChanged;
        messenger.InternetRoute.Changed -= OnInternetRouteChanged;
        messenger.Groups.Changed -= OnGroupChanged;
        messenger.GroupChats.Changed -= OnGroupChatChanged;
        messenger.CopiedChats.Changed -= OnCopiedChatChanged;
    }

    [RelayCommand]
    private void AddInPerson() => navigation.Open(pages.AddInPerson());

    [RelayCommand]
    private void AddContact() => navigation.Open(pages.AddContact());

    [RelayCommand]
    private void MyCode() => navigation.Open(pages.Profile());

    [RelayCommand]
    private void NewGroup() => navigation.Open(pages.NewGroup());

    [RelayCommand]
    private void OpenMenu() => IsMenuOpen = true;

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    [RelayCommand]
    private void SendPcCopy()
    {
        IsMenuOpen = false;
        navigation.Open(pages.SendCopy());
    }

    [RelayCommand]
    private void ReceivePcCopy()
    {
        IsMenuOpen = false;
        navigation.Open(pages.ReceiveCopy());
    }

    [RelayCommand]
    private void Network()
    {
        IsMenuOpen = false;
        navigation.Open(pages.Network());
    }

    [RelayCommand]
    private void Passphrase()
    {
        IsMenuOpen = false;
        navigation.Open(pages.Passphrase());
    }

    [RelayCommand]
    private void Background()
    {
        IsMenuOpen = false;
        navigation.Open(pages.Background(false));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void DeleteAllData()
    {
        IsMenuOpen = false;
        DeleteAll.OpenCommand.Execute(null);
    }

    private void Open(Contact contact) => navigation.Open(pages.Conversation(contact));

    private void OpenGroup(Group group) => navigation.Open(pages.GroupConversation(group.Id));

    private void OpenCopiedChat(CopiedConversation conversation) => navigation.Open(pages.CopiedConversation(conversation.Origin));

    private void OnGroupChanged(object? sender, GroupChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnGroupChatChanged(object? sender, GroupChatChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnCopiedChatChanged(object? sender, CopiedChatChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnContactChanged(object? sender, ContactChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnChatChanged(object? sender, ChatChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnInternetRouteChanged(object? sender, EventArgs e) => uiThread.Post(RefreshInternetRoute);

    private bool CanRunTorSwitch => !IsSwitchingTor;

    [RelayCommand(CanExecute = nameof(CanRunTorSwitch))]
    private async Task SwitchTorAsync(CancellationToken cancellationToken)
    {
        if (setTor is null)
        {
            return;
        }

        IsSwitchingTor = true;
        try
        {
            await setTor(!messenger.InternetRoute.IsOn, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            IsSwitchingTor = false;
            RefreshInternetRoute();
        }
    }

    public void SetSelection(PageViewModel? open)
    {
        object? selected = open switch
        {
            ConversationViewModel conversation => conversation.ContactKey,
            GroupConversationViewModel group => group.GroupId,
            CopiedConversationViewModel copied => copied.Origin,
            _ => null,
        };
        if (Equals(selected, selection))
        {
            return;
        }

        selection = selected;
        RefreshContacts();
    }

    private void RefreshInternetRoute()
    {
        InternetRouteText = InternetRouteLines.Describe(messenger.InternetRoute.Status);
        CanSwitchTor = setTor is not null && messenger.InternetRoute.IsInBuild;
        TorSwitchText = messenger.InternetRoute.IsOn ? UiText.ContactListTurnTorOff : UiText.ContactListTurnTorOn;
    }

    private void Refresh()
    {
        RefreshContacts();
        HasNoNetwork = messenger.Connections.Network is { HasAnyNetwork: false };
        RefreshInternetRoute();
    }

    private void RefreshContacts()
    {
        string filter = SearchText.Trim();
        IReadOnlyList<Group> groups = messenger.Groups.All;
        Groups.Clear();
        foreach (Group group in groups.OrderBy(static group => group.Name.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            if (filter.Length == 0 || group.Name.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                Groups.Add(new GroupItemViewModel(group, messenger, OpenGroup) { IsSelected = selection is GroupId selectedGroup && selectedGroup.Equals(group.Id) });
            }
        }

        IReadOnlyList<Contact> all = messenger.Contacts.All;
        Contacts.Clear();
        foreach (Contact contact in all)
        {
            if (filter.Length == 0 || contact.Name.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                Contacts.Add(new ContactItemViewModel(contact, messenger, Open) { IsSelected = selection is Ed25519PublicKey selectedContact && selectedContact.Equals(contact.IdentityKey) });
            }
        }

        IReadOnlyList<CopiedConversation> copiedAll = messenger.CopiedChats.All;
        CopiedChats.Clear();
        foreach (CopiedConversation copied in copiedAll)
        {
            if (filter.Length == 0 || copied.Title.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                CopiedChats.Add(new CopiedChatItemViewModel(copied, timeZone, OpenCopiedChat) { IsSelected = selection is CopyOriginId selectedCopy && selectedCopy.Equals(copied.Origin) });
            }
        }

        Chats.Clear();
        foreach (IChatItem chat in Groups.Cast<IChatItem>().Concat(Contacts)
            .OrderByDescending(static chat => chat.LastActivity ?? DateTimeOffset.MinValue)
            .ThenBy(static chat => chat.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Chats.Add(chat);
        }

        OnPropertyChanged(nameof(HasCopiedChats));
        HasContacts = all.Count > 0;
        HasSearchableItems = all.Count + groups.Count + copiedAll.Count > 0;
        IsEmpty = !HasSearchableItems;
        HasNoMatches = HasSearchableItems && Contacts.Count + Groups.Count + CopiedChats.Count == 0;
    }

    partial void OnSearchTextChanged(string value) => RefreshContacts();
}
