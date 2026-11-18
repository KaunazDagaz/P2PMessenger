using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class NetworkViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IUiThread uiThread;

    [ObservableProperty]
    private string listeningText = string.Empty;

    [ObservableProperty]
    private string portText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ListenCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string addressesText = string.Empty;

    [ObservableProperty]
    private bool hasNoContacts;

    public NetworkViewModel(Messenger messenger, IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(uiThread);

        this.messenger = messenger;
        this.uiThread = uiThread;
        int port = messenger.Connections.ListeningPort ?? messenger.Connections.LastRequestedPort ?? MessengerSetup.DefaultPort;
        portText = port.ToString(CultureInfo.InvariantCulture);
        if (messenger.Connections.ListeningPort is null && messenger.Connections.LastListenResult == ListenResult.PortInUse)
        {
            errorMessage = Formats.With(Formats.PortInUse, port);
        }

        RefreshListening();
        RefreshContacts();
    }

    public override string Title => UiText.NetworkTitle;

    public ObservableCollection<NetworkContactViewModel> Contacts { get; } = [];

    private bool IsIdle => !IsBusy;

    public override void OnShown()
    {
        messenger.Connections.Changed += OnConnectionChanged;
        messenger.Contacts.Changed += OnContactChanged;
        RefreshListening();
        RefreshContacts();
    }

    public override void OnHidden()
    {
        messenger.Connections.Changed -= OnConnectionChanged;
        messenger.Contacts.Changed -= OnContactChanged;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(PortText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > IPEndPoint.MaxPort)
        {
            ErrorMessage = UiText.NetworkInvalidPort;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ListenResult result = await messenger.Connections.ListenAsync(IPAddress.Any, port, cancellationToken);
            ErrorMessage = result switch
            {
                ListenResult.Listening => null,
                ListenResult.PortInUse => Formats.With(Formats.PortInUse, port),
                ListenResult.InvalidPort => UiText.NetworkInvalidPort,
                _ => UiText.NetworkNotListening,
            };
            RefreshListening();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e) => uiThread.Post(() =>
    {
        RefreshListening();
        foreach (NetworkContactViewModel contact in Contacts)
        {
            contact.Refresh();
        }
    });

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (e.Change is ContactChange.Added or ContactChange.KeysReplaced)
        {
            uiThread.Post(RefreshContacts);
        }
    }

    private void RefreshListening()
    {
        int? port = messenger.Connections.ListeningPort;
        ListeningText = port is { } listening ? Formats.With(Formats.Listening, listening) : UiText.NetworkNotListening;
        IReadOnlyList<IPAddress> addresses = LocalAddresses.Find();
        AddressesText = addresses.Count == 0
            ? UiText.NetworkNoAddresses
            : string.Join(Environment.NewLine, addresses.Select(address => port is { } p ? new IPEndPoint(address, p).ToString() : address.ToString()));
    }

    private void RefreshContacts()
    {
        Contacts.Clear();
        foreach (Contact contact in messenger.Contacts.All)
        {
            Contacts.Add(new NetworkContactViewModel(messenger, contact));
        }

        HasNoContacts = Contacts.Count == 0;
    }
}
