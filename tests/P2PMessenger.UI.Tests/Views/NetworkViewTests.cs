using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class NetworkViewTests
{
    [AvaloniaFact]
    public async Task Network_ShowsTheListeningPortAndTakesContactAddresses()
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
        var contact = Assert.Single(page.Contacts);
        Assert.Equal(UiText.DialNoAddress, contact.StatusText);

        contact.AddressText = "nonsense";
        contact.SaveCommand.Execute(null);
        Assert.Equal(UiText.AddContactAddressInvalid, contact.ErrorMessage);

        contact.AddressText = " 127.0.0.1:1 ";
        contact.SaveCommand.Execute(null);
        Assert.Null(contact.ErrorMessage);
        Assert.Equal("127.0.0.1:1", contact.AddressText);
        Assert.True(harness.Messenger.Connections.TryGetAddress(bob.IdentityKey, out _));

        contact.ClearCommand.Execute(null);
        Assert.False(harness.Messenger.Connections.TryGetAddress(bob.IdentityKey, out _));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Network_InvalidPort_IsRefused()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Network();
        var view = new NetworkView { DataContext = page };
        harness.Show(view);
        int? before = harness.Messenger.Connections.ListeningPort;

        page.PortText = "70000";
        await page.ListenCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.NetworkInvalidPort, UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Equal(before, harness.Messenger.Connections.ListeningPort);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Network_PortTakenAtStart_SaysWhyTheDeviceIsNotListening()
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
        Assert.Equal($"Port {taken} is used by another program. Choose another port.", UiHarness.Find<TextBlock>(view, "ErrorText").Text);
        Assert.Equal(taken.ToString(System.Globalization.CultureInfo.InvariantCulture), page.PortText);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-02")]
    public async Task Network_FirstConnectionRefused_ShowsEveryPossibleReason()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var bob = await UiHarness.StartPeerAsync("Bob");
        Assert.True((await harness.Messenger.Contacts.AddAsync(harness.Messenger.Contacts.Check(bob.Identity.CodeText), CancellationToken.None)).IsSaved);
        var page = harness.Shell.Pages.Network();
        harness.Shell.Push(page);
        harness.Show(new NetworkView { DataContext = page });
        var contact = Assert.Single(page.Contacts);

        contact.AddressText = UiHarness.EndpointOf(bob).ToString();
        contact.SaveCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => contact.StatusText == UiText.DialRefused);

        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }
}
