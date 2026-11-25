using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Core.Identity;
using P2PMessenger.Tests.Shared;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class NetworkViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-06")]
    public async Task Network_ShowsThePortsTheInterfacesAndWhereEachContactWasHeard()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var bob = KeySet.Create();
        await harness.AddContactAsync(bob, "Bob");
        var page = harness.Shell.Pages.Network();
        harness.Shell.Push(page);
        var view = new NetworkView { DataContext = page };
        harness.Show(view);

        int port = harness.Messenger.Connections.ListeningPort ?? 0;
        Assert.Equal($"This device listens on port {port}.", UiHarness.Find<TextBlock>(view, "ListeningText").Text);
        Assert.Equal(
            $"This device announces itself and listens for contacts on UDP port {LoopbackDiscovery.FastOptions.Port}.",
            UiHarness.Find<TextBlock>(view, "DiscoveryText").Text);
        Assert.Equal(LoopbackDiscovery.Interface.ToString(), UiHarness.Find<SelectableTextBlock>(view, "AddressesText").Text);
        var contact = Assert.Single(page.Contacts);
        Assert.Equal(UiText.NetworkNotHeard, contact.HeardText);
        Assert.Equal(UiText.ContactWaiting, contact.StatusText);

        await using (harness.HearFrom(bob, 50_000))
        {
            await UiHarness.WaitUntilAsync(() => contact.HeardText == "Heard at 127.0.0.1:50000");
        }

        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-07")]
    public async Task Network_ConnectedContact_IsReachableNearby()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await harness.ConnectPeerAsync("Bob");
        var page = harness.Shell.Pages.Network();
        harness.Shell.Push(page);
        harness.Show(new NetworkView { DataContext = page });

        var contact = Assert.Single(page.Contacts);
        await UiHarness.WaitUntilAsync(() => contact.StatusText == UiText.ContactReachableNearby && contact.HeardText != UiText.NetworkNotHeard);
        Assert.StartsWith("Heard at 127.0.0.1:", contact.HeardText, StringComparison.Ordinal);

        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-07")]
    [Trait("Story", "US-09")]
    public async Task Network_ContactConnectedOverTor_SaysSoAndBeforeTorSaysThatTorIsNotTried()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        await using var tor = new FakeTorRoute();
        using var carol = KeySet.Create();
        await harness.AddContactAsync(carol, "Carol");
        var page = harness.Shell.Pages.Network();
        harness.Shell.Push(page);
        harness.Show(new NetworkView { DataContext = page });
        Assert.Equal(UiText.NetworkTorNotUsed, Assert.Single(page.Contacts).TorText);

        var bob = await harness.ConnectPeerOverTorAsync("Bob", tor);
        await UiHarness.WaitUntilAsync(() => page.Contacts.Count == 2);
        var bobItem = page.Contacts.Single(static contact => contact.Name == "Bob");
        var carolItem = page.Contacts.Single(static contact => contact.Name == "Carol");
        await UiHarness.WaitUntilAsync(() => bobItem.StatusText == UiText.ContactReachableOverTor && bobItem.TorText == UiText.NetworkTorConnected);
        await UiHarness.WaitUntilAsync(() => carolItem.TorText == UiText.NetworkTorNotFound);

        Assert.Equal(UiText.ContactWaiting, carolItem.StatusText);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Network_PortTaken_SaysThatTheDeviceIsNotListening()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        using var other = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        other.Start();
        int taken = ((System.Net.IPEndPoint)other.LocalEndpoint).Port;
        await harness.Messenger.Connections.ListenAsync(System.Net.IPAddress.Loopback, taken, CancellationToken.None);

        var page = harness.Shell.Pages.Network();
        var view = new NetworkView { DataContext = page };
        harness.Show(view);

        Assert.Equal(UiText.NetworkNotListening, UiHarness.Find<TextBlock>(view, "ListeningText").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Network_WithoutALocalInterface_SaysSo()
    {
        await using var harness = await UiHarness.StartAsync("Alice", Transport.Discovery.NetworkSnapshot.None);
        var page = harness.Shell.Pages.Network();
        var view = new NetworkView { DataContext = page };
        harness.Show(view);

        Assert.Equal(UiText.NetworkNoAddresses, UiHarness.Find<SelectableTextBlock>(view, "AddressesText").Text);
        harness.Messenger.Identity.Dispose();
    }
}
