using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.Application;
using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Internet;
using P2PMessenger.Transport.Discovery;
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
    private string discoveryText = string.Empty;

    [ObservableProperty]
    private string addressesText = string.Empty;

    [ObservableProperty]
    private string torText = string.Empty;

    [ObservableProperty]
    private bool hasNoContacts;

    public NetworkViewModel(Messenger messenger, IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(uiThread);

        this.messenger = messenger;
        this.uiThread = uiThread;
        RefreshDevice();
        RefreshContacts();
    }

    public override string Title => UiText.NetworkTitle;

    public ObservableCollection<NetworkContactViewModel> Contacts { get; } = [];

    public override void OnShown()
    {
        messenger.Connections.Changed += OnConnectionChanged;
        messenger.Contacts.Changed += OnContactChanged;
        messenger.InternetRoute.Changed += OnInternetRouteChanged;
        RefreshDevice();
        RefreshContacts();
    }

    public override void OnHidden()
    {
        messenger.Connections.Changed -= OnConnectionChanged;
        messenger.Contacts.Changed -= OnContactChanged;
        messenger.InternetRoute.Changed -= OnInternetRouteChanged;
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e) => uiThread.Post(() =>
    {
        RefreshDevice();
        foreach (NetworkContactViewModel contact in Contacts)
        {
            contact.Refresh();
        }
    });

    private void OnInternetRouteChanged(object? sender, EventArgs e) => uiThread.Post(RefreshDevice);

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (e.Change is ContactChange.Added or ContactChange.KeysReplaced)
        {
            uiThread.Post(RefreshContacts);
        }
    }

    private void RefreshDevice()
    {
        ConnectionService connections = messenger.Connections;
        ListeningText = connections.ListeningPort is { } port ? Formats.With(Formats.Listening, port) : UiText.NetworkNotListening;
        DiscoveryText = connections.DiscoveryState switch
        {
            DiscoveryState.Running => Formats.With(Formats.Discovery, connections.DiscoveryPort),
            DiscoveryState.Unavailable => Formats.With(Formats.DiscoveryUnavailable, connections.DiscoveryPort),
            _ => UiText.NetworkDiscoveryOff,
        };
        IReadOnlyList<LocalInterface> interfaces = connections.Network?.Interfaces ?? [];
        AddressesText = interfaces.Count == 0
            ? UiText.NetworkNoAddresses
            : string.Join(Environment.NewLine, interfaces.Select(static item => item.ToString()));
        InternetRouteStatus tor = messenger.InternetRoute.Status;
        TorText = Formats.WithAll(Formats.Tor, tor.State, tor.Progress, tor.Problem, tor.OnionServicePublished, messenger.InternetRoute.TargetKind);
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
