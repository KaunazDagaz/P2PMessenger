using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.Background;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.Internet;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

[Trait("Story", "US-09")]
public sealed class TorSwitchViewTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("p2pm-ui-switch-").FullName;
    private readonly InMemorySettingsStore settings = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [AvaloniaFact]
    public async Task ContactList_TorInTheBuild_TurnsTorOffAndOnAgainAndRemembersIt()
    {
        await using var harness = await StartAsync();
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);
        var button = UiHarness.Find<Button>(view, "SwitchTorButton");
        Assert.True(button.IsVisible);
        Assert.Equal(UiText.ContactListTurnTorOff, button.Content);

        await page.SwitchTorCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Equal(UiText.ContactListTorOff, UiHarness.Find<TextBlock>(view, "InternetRouteText").Text);
        Assert.Equal(UiText.ContactListTurnTorOn, button.Content);
        Assert.False(new InternetRouteSettings(settings).UseTor);
        Assert.False(harness.Messenger.InternetRoute.IsOn);
        Assert.False(Directory.Exists(TorData));

        await page.SwitchTorCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(new InternetRouteSettings(settings).UseTor);
        Assert.True(harness.Messenger.InternetRoute.IsOn);
        Assert.Equal(UiText.ContactListTurnTorOff, button.Content);
        Assert.NotEqual(UiText.ContactListTorOff, UiHarness.Find<TextBlock>(view, "InternetRouteText").Text);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ContactList_TorOffInTheSettings_StartsWithTorOff()
    {
        new InternetRouteSettings(settings).UseTor = false;

        await using var harness = await StartAsync();
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };
        harness.Show(view);

        Assert.Equal(InternetRouteStatus.Off, harness.Messenger.InternetRoute.Status);
        Assert.Null(harness.Messenger.InternetRoute.TargetKind);
        Assert.Equal(UiText.ContactListTorOff, UiHarness.Find<TextBlock>(view, "InternetRouteText").Text);
        Assert.Equal(UiText.ContactListTurnTorOn, UiHarness.Find<Button>(view, "SwitchTorButton").Content);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task ContactList_TorNotInTheBuild_HasNoSwitch()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var page = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        var view = new ContactListView { DataContext = page };

        harness.Show(view);

        Assert.False(UiHarness.Find<Button>(view, "SwitchTorButton").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task CreateIdentity_TorUnchecked_StartsTheMessengerWithTorOffAndRemembersIt()
    {
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, null, Setup(), settings);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var view = new CreateIdentityView { DataContext = page };
        harness.Show(view);
        Assert.True(UiHarness.Find<StackPanel>(view, "TorPanel").IsVisible);
        var box = UiHarness.Find<CheckBox>(view, "UseTorCheckBox");
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        page.Name = "Alice";
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.False(page.UseTor);
        Assert.False(new InternetRouteSettings(settings).UseTor);
        Assert.Equal(InternetRouteStatus.Off, harness.Messenger.InternetRoute.Status);
        Assert.False(Directory.Exists(TorData));
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    public async Task CreateIdentity_TorNotInTheBuild_DoesNotAsk()
    {
        await using var harness = UiHarness.Create();
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var view = new CreateIdentityView { DataContext = page };

        harness.Show(view);

        Assert.False(UiHarness.Find<StackPanel>(view, "TorPanel").IsVisible);
        Assert.False(page.OffersTor);
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteAllData_TurnsTorBackOnForTheNextIdentity()
    {
        await using var harness = await StartAsync();
        await harness.Services.SetTorAsync(false, CancellationToken.None);
        Assert.False(new InternetRouteSettings(settings).UseTor);

        Assert.True(await harness.Services.DeleteAllDataAsync(CancellationToken.None));

        Assert.True(new InternetRouteSettings(settings).UseTor);
    }

    private string TorData => Path.Combine(folder, "tor");

    private TorSetup Setup()
    {
        var executable = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName, OperatingSystem.IsWindows() ? "tor.exe" : "tor");
        File.WriteAllText(executable, "not a program");
        return TorSetup.For(executable, TorData);
    }

    private async Task<UiHarness> StartAsync()
    {
        var harness = UiHarness.Create(new InMemoryLocalData(), null, null, Setup(), settings);
        Assert.True((await harness.Services.OpenLocalDataAsync(CancellationToken.None)).IsOpen);
        CreateIdentityResult created = await harness.Services.Identities.CreateAsync("Alice", CancellationToken.None);
        Assert.True(created.IsCreated);
        await harness.Shell.StartAsync(CancellationToken.None);
        Assert.NotNull(harness.Services.Messenger);
        return harness;
    }
}
