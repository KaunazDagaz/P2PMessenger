using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application;
using P2PMessenger.Application.Internet;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

[Trait("Story", "US-10")]
public sealed class InternetRouteViewTests
{
    [AvaloniaFact]
    public async Task ContactList_TorNotInTheBuild_SaysTheInternetRouteIsUnavailable()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };

        harness.Show(view);

        var line = UiHarness.Find<TextBlock>(view, "InternetRouteText");
        Assert.True(line.IsVisible);
        Assert.Equal(UiText.ContactListTorNotInBuild, line.Text);
        Assert.Equal("Internet route unavailable: Tor is not part of this build.", line.Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task Network_ShowsTheTorState()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = harness.Shell.Pages.Network();
        harness.Shell.Push(page);
        var view = new NetworkView { DataContext = page };

        harness.Show(view);

        Assert.Equal("Tor: NotInBuild, 0 %, problem: NotInBuild, onion service published: False, onion target: .", UiHarness.Find<TextBlock>(view, "TorText").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteAllData_AlsoDeletesTorsFolder()
    {
        var folder = Directory.CreateTempSubdirectory("p2pm-ui-tor-").FullName;
        try
        {
            var tor = TorSetup.For(null, Path.Combine(folder, "tor"));
            Directory.CreateDirectory(Path.Combine(tor.DataFolder, "keys"));
            await File.WriteAllTextAsync(Path.Combine(tor.DataFolder, "state"), "guards", TestContext.Current.CancellationToken);
            var platform = new FakePlatform();
            var setup = MessengerSetup.InMemory() with { ListenAddress = IPAddress.Loopback, ListenPort = 0, Tor = tor };
            await using var services = new AppServices(new InMemoryLocalData(), setup, new AvaloniaUiThread(), platform, platform, platform);
            Assert.True((await services.OpenLocalDataAsync(CancellationToken.None)).IsOpen);
            Assert.True((await services.Identities.CreateAsync("Alice", CancellationToken.None)).IsCreated);

            Assert.True(await services.DeleteAllDataAsync(CancellationToken.None));

            Assert.False(Directory.Exists(tor.DataFolder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Describe_EveryState_GivesItsOwnLine()
    {
        Assert.Equal(UiText.ContactListTorConnected, InternetRouteLines.Describe(new InternetRouteStatus(InternetRouteState.Connected, 100, InternetRouteProblem.None, true)));
        Assert.Equal("Connecting to Tor (45 %).", InternetRouteLines.Describe(new InternetRouteStatus(InternetRouteState.Starting, 45, InternetRouteProblem.None, false)));
        Assert.Equal(UiText.ContactListTorNotInBuild, InternetRouteLines.Describe(InternetRouteStatus.NotInBuild));
    }

    [Theory]
    [InlineData(InternetRouteProblem.NoNetwork, nameof(UiText.ContactListTorNoNetwork))]
    [InlineData(InternetRouteProblem.ClockWrong, nameof(UiText.ContactListTorClockWrong))]
    [InlineData(InternetRouteProblem.CannotReachTor, nameof(UiText.ContactListTorCannotReach))]
    [InlineData(InternetRouteProblem.TorFailed, nameof(UiText.ContactListTorFailed))]
    [InlineData(InternetRouteProblem.TorStopped, nameof(UiText.ContactListTorStopped))]
    public void Describe_Unavailable_SaysWhy(InternetRouteProblem problem, string resource)
    {
        var line = InternetRouteLines.Describe(new InternetRouteStatus(InternetRouteState.Unavailable, 10, problem, false));

        Assert.Equal(UiText.ResourceManager.GetString(resource, UiText.Culture), line);
        Assert.StartsWith("Internet route unavailable: ", line, StringComparison.Ordinal);
    }
}
