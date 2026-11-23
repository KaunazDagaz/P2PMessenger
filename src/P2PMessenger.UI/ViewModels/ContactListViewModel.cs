using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ContactListViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private readonly Pages pages;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool hasNoNetwork;

    public ContactListViewModel(Messenger messenger, IUiThread uiThread, INavigationService navigation, Pages pages, bool offersPassphrase)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);

        this.messenger = messenger;
        this.uiThread = uiThread;
        this.navigation = navigation;
        this.pages = pages;
        OffersPassphrase = offersPassphrase;
        Refresh();
    }

    public override string Title => UiText.ContactListTitle;

    public bool OffersPassphrase { get; }

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public override void OnShown()
    {
        messenger.Contacts.Changed += OnContactChanged;
        messenger.Chats.Changed += OnChatChanged;
        messenger.Connections.Changed += OnConnectionChanged;
        Refresh();
    }

    public override void OnHidden()
    {
        messenger.Contacts.Changed -= OnContactChanged;
        messenger.Chats.Changed -= OnChatChanged;
        messenger.Connections.Changed -= OnConnectionChanged;
    }

    [RelayCommand]
    private void AddContact() => navigation.Push(pages.AddContact());

    [RelayCommand]
    private void MyCode() => navigation.Push(pages.Profile());

    [RelayCommand]
    private void Network() => navigation.Push(pages.Network());

    [RelayCommand]
    private void Passphrase() => navigation.Push(pages.Passphrase());

    [RelayCommand]
    private void DeleteAllData() => navigation.Push(pages.DeleteAllData());

    private void Open(Contact contact) => navigation.Push(pages.Conversation(contact));

    private void OnContactChanged(object? sender, ContactChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnChatChanged(object? sender, ChatChangedEventArgs e) => uiThread.Post(Refresh);

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e) => uiThread.Post(Refresh);

    private void Refresh()
    {
        Contacts.Clear();
        foreach (Contact contact in messenger.Contacts.All)
        {
            Contacts.Add(new ContactItemViewModel(contact, messenger, Open));
        }

        IsEmpty = Contacts.Count == 0;
        HasNoNetwork = messenger.Connections.Network is { HasAnyNetwork: false };
    }
}
