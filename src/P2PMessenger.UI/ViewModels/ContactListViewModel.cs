using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
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
        Refresh();
    }

    public override string Title => UiText.ContactListTitle;

    public bool OffersPassphrase { get; }

    public bool OffersBackground { get; }

    public bool OffersSendPcCopy { get; }

    public bool OffersReceivePcCopy { get; }

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public ObservableCollection<GroupItemViewModel> Groups { get; } = [];

    public ObservableCollection<CopiedChatItemViewModel> CopiedChats { get; } = [];

    public bool HasGroups => Groups.Count > 0;

    public bool HasCopiedChats => CopiedChats.Count > 0;

    public bool ShowContactsHeading => (HasGroups || HasCopiedChats) && Contacts.Count > 0;

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
        messenger.Contacts.Changed -= OnContactChanged;
        messenger.Chats.Changed -= OnChatChanged;
        messenger.Connections.Changed -= OnConnectionChanged;
        messenger.InternetRoute.Changed -= OnInternetRouteChanged;
        messenger.Groups.Changed -= OnGroupChanged;
        messenger.GroupChats.Changed -= OnGroupChatChanged;
        messenger.CopiedChats.Changed -= OnCopiedChatChanged;
    }

    [RelayCommand]
    private void AddInPerson() => navigation.Push(pages.AddInPerson());

    [RelayCommand]
    private void AddContact() => navigation.Push(pages.AddContact());

    [RelayCommand]
    private void MyCode() => navigation.Push(pages.Profile());

    [RelayCommand]
    private void NewGroup() => navigation.Push(pages.NewGroup());

    [RelayCommand]
    private void SendPcCopy() => navigation.Push(pages.SendCopy());

    [RelayCommand]
    private void ReceivePcCopy() => navigation.Push(pages.ReceiveCopy());

    [RelayCommand]
    private void Network() => navigation.Push(pages.Network());

    [RelayCommand]
    private void Passphrase() => navigation.Push(pages.Passphrase());

    [RelayCommand]
    private void Background() => navigation.Push(pages.Background(false));

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void DeleteAllData() => navigation.Push(pages.DeleteAllData());

    private void Open(Contact contact) => navigation.Push(pages.Conversation(contact));

    private void OpenGroup(Group group) => navigation.Push(pages.GroupConversation(group.Id));

    private void OpenCopiedChat(CopiedConversation conversation) => navigation.Push(pages.CopiedConversation(conversation.Origin));

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
                Groups.Add(new GroupItemViewModel(group, messenger, OpenGroup));
            }
        }

        IReadOnlyList<Contact> all = messenger.Contacts.All;
        Contacts.Clear();
        foreach (Contact contact in all)
        {
            if (filter.Length == 0 || contact.Name.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                Contacts.Add(new ContactItemViewModel(contact, messenger, Open));
            }
        }

        IReadOnlyList<CopiedConversation> copiedAll = messenger.CopiedChats.All;
        CopiedChats.Clear();
        foreach (CopiedConversation copied in copiedAll)
        {
            if (filter.Length == 0 || copied.Title.Value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                CopiedChats.Add(new CopiedChatItemViewModel(copied, timeZone, OpenCopiedChat));
            }
        }

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasCopiedChats));
        OnPropertyChanged(nameof(ShowContactsHeading));
        HasContacts = all.Count > 0;
        HasSearchableItems = all.Count + groups.Count + copiedAll.Count > 0;
        IsEmpty = !HasSearchableItems;
        HasNoMatches = HasSearchableItems && Contacts.Count + Groups.Count + CopiedChats.Count == 0;
    }

    partial void OnSearchTextChanged(string value) => RefreshContacts();
}
