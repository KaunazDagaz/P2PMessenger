using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Crypto;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class BackgroundViewTests
{
    [AvaloniaFact]
    public async Task StartOnce_CalledTwice_StartsOnce()
    {
        int starts = 0;
        var once = new StartOnce(_ =>
        {
            starts++;
            return Task.CompletedTask;
        });

        Assert.False(once.HasStarted);
        var first = once.RunAsync(CancellationToken.None);
        var second = once.RunAsync(CancellationToken.None);
        await first;

        Assert.Same(first, second);
        Assert.Equal(1, starts);
        Assert.True(once.HasStarted);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task MessengerStarted_ExemptAndAllowed_StartsTheServiceAndShowsTheContacts()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);

        await StartWithIdentityAsync(harness, "Alice");

        Assert.True(fake.IsRunning);
        Assert.Equal([nameof(FakeBackground.StartRunning)], fake.Calls);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task MessengerStarted_NotExempt_OffersTheBackgroundPageOnce()
    {
        var fake = new FakeBackground();
        var ports = fake.Ports();
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, ports);
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);

        await StartWithIdentityAsync(harness, "Alice");

        var page = Assert.IsType<BackgroundViewModel>(harness.Shell.CurrentPage);
        Assert.True(page.IsFirstRun);
        Assert.True(ports.Settings.WasOffered);
        Assert.False(ports.ShouldOffer);
        Assert.True(harness.Shell.CanGoBack);
        page.ContinueCommand.Execute(null);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task AllowBattery_Refused_ExplainsThatMessagesMayBeDelayedAndKeepsWorking()
    {
        var fake = new FakeBackground { IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = Assert.IsType<BackgroundViewModel>(harness.Shell.CurrentPage);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);

        Assert.True(UiHarness.Find<Button>(view, "AllowBatteryButton").IsVisible);
        await page.AllowBatteryCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Contains("RequestExemption", fake.Calls);
        Assert.False(page.IsExempt);
        Assert.True(page.BatteryRefused);
        Assert.True(UiHarness.Find<TextBlock>(view, "BatteryRefusedText").IsVisible);
        Assert.Contains("may delay messages while the phone is idle", UiHarness.Find<TextBlock>(view, "BatteryText").Text, StringComparison.Ordinal);
        Assert.Contains("keeps working", UiText.BackgroundBatteryRefused, StringComparison.Ordinal);
        Assert.True(fake.IsRunning);
        Assert.NotNull(harness.Services.Messenger);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task AllowBattery_Granted_SaysSoAndHidesTheButton()
    {
        var fake = new FakeBackground { IsAllowed = true, GrantsExemption = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = Assert.IsType<BackgroundViewModel>(harness.Shell.CurrentPage);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);

        await page.AllowBatteryCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(page.IsExempt);
        Assert.False(page.BatteryRefused);
        Assert.False(UiHarness.Find<Button>(view, "AllowBatteryButton").IsVisible);
        Assert.Equal(UiText.BackgroundBatteryExempt, UiHarness.Find<TextBlock>(view, "BatteryText").Text);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Restricted_ByAndroid_SaysThatTheAppStopsAndOffersTheSettings()
    {
        var fake = new FakeBackground { IsAllowed = true, IsRestricted = true, LiftsRestriction = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = Assert.IsType<BackgroundViewModel>(harness.Shell.CurrentPage);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);

        Assert.True(page.IsRestricted);
        Assert.Equal(UiText.BackgroundRestricted, UiHarness.Find<TextBlock>(view, "BatteryText").Text);
        Assert.Contains("stops soon after you leave it", UiText.BackgroundRestricted, StringComparison.Ordinal);
        Assert.DoesNotContain("keeps working", UiText.BackgroundRestricted, StringComparison.Ordinal);
        Assert.True(UiHarness.Find<Button>(view, "OpenSettingsButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "AllowBatteryButton").IsVisible);

        await page.OpenSettingsCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Contains("OpenSettings", fake.Calls);
        Assert.False(page.IsRestricted);
        Assert.False(UiHarness.Find<Button>(view, "OpenSettingsButton").IsVisible);
        Assert.True(UiHarness.Find<Button>(view, "AllowBatteryButton").IsVisible);
        Assert.Equal(UiText.BackgroundBatteryNotExempt, UiHarness.Find<TextBlock>(view, "BatteryText").Text);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Restricted_SettingsLeftAsTheyWere_StillSaysSo()
    {
        var fake = new FakeBackground { IsAllowed = true, IsRestricted = true };
        var page = new BackgroundViewModel(fake.Ports(), () => Task.CompletedTask, new NoNavigation(), false);

        await page.OpenSettingsCommand.ExecuteAsync(null);

        Assert.True(page.IsRestricted);
        Assert.False(page.CanAskBattery);
        Assert.Equal(UiText.BackgroundRestricted, page.BatteryText);
        Assert.False(page.IsBusy);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task EnsureRunning_ServiceStoppedByAndroidWhileTheMessengerRuns_StartsItAgain()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        fake.StoppedByAndroid();

        coordinator.EnsureRunning();

        Assert.True(fake.IsRunning);
        Assert.Equal([nameof(FakeBackground.StartRunning), nameof(FakeBackground.StartRunning)], fake.Calls);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task EnsureRunning_NoMessenger_StartsNothing()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);

        coordinator.EnsureRunning();

        Assert.False(fake.IsRunning);
        Assert.Empty(fake.Calls);
    }

    [AvaloniaFact]
    [Trait("Story", "US-19")]
    public async Task AllowNotifications_Granted_SaysThatNotificationsAreOn()
    {
        var fake = new FakeBackground { IsExempt = true, GrantsNotifications = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = Assert.IsType<BackgroundViewModel>(harness.Shell.CurrentPage);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);

        Assert.Equal(UiText.BackgroundNotificationsOff, UiHarness.Find<TextBlock>(view, "NotificationsText").Text);
        await page.AllowNotificationsCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.True(page.NotificationsAllowed);
        Assert.Equal(UiText.BackgroundNotificationsAllowed, UiHarness.Find<TextBlock>(view, "NotificationsText").Text);
        Assert.False(UiHarness.Find<Button>(view, "AllowNotificationsButton").IsVisible);
    }

    [AvaloniaFact]
    [Trait("Story", "US-19")]
    public async Task IncomingMessage_AppInTheBackground_NotifiesWhoSentItWithoutTheText()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true, IsVisible = false };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var bob = await harness.ConnectPeerAsync("Bob");

        await WaitUntilAddedAsync(bob, harness.Messenger.Identity.Keys.IdentityKey);
        Assert.True((await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "meet at the gate", CancellationToken.None)).IsQueued);
        await UiHarness.WaitUntilAsync(() => fake.Shown.Count > 0);

        var notice = Assert.Single(fake.Shown);
        Assert.Equal("Bob", notice.SenderName);
        Assert.Null(notice.Preview);
        await UiHarness.StopPeerAsync(bob);
    }

    [AvaloniaFact]
    [Trait("Story", "US-19")]
    public async Task Previews_TurnedOnOnThePage_NotificationShowsTheText()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true, IsVisible = false };
        var ports = fake.Ports();
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, ports);
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = harness.Shell.Pages.Background(false);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);
        var previews = UiHarness.Find<CheckBox>(view, "PreviewsBox");
        Assert.False(previews.IsChecked);

        previews.IsChecked = true;
        UiHarness.Flush();
        var bob = await harness.ConnectPeerAsync("Bob");
        await WaitUntilAddedAsync(bob, harness.Messenger.Identity.Keys.IdentityKey);
        Assert.True((await bob.SendAsync(harness.Messenger.Identity.Keys.IdentityKey, "meet at the gate", CancellationToken.None)).IsQueued);
        await UiHarness.WaitUntilAsync(() => fake.Shown.Count > 0);

        Assert.True(ports.Settings.ShowPreviews);
        Assert.Equal("meet at the gate", Assert.Single(fake.Shown).Preview);
        Assert.Equal(UiText.BackgroundPreviewsHint, UiHarness.Find<TextBlock>(view, "PreviewsHintText").Text);
        await UiHarness.StopPeerAsync(bob);
    }

    [AvaloniaFact]
    [Trait("Story", "US-21")]
    public async Task DeleteAllData_StopsTheServiceAndResetsTheSettings()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        var ports = fake.Ports();
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, ports);
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        ports.Settings.ShowPreviews = true;

        Assert.True(await harness.Shell.DeleteAllDataAsync(CancellationToken.None));

        Assert.False(fake.IsRunning);
        Assert.Equal([nameof(FakeBackground.StartRunning), nameof(FakeBackground.StopRunning)], fake.Calls);
        Assert.False(ports.Settings.ShowPreviews);
        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Resume_SavedDataWithoutPassphrase_StartsTheMessengerWithoutAWindow()
    {
        using var data = new SavedData();
        await CreateSavedIdentityAsync(data, null);
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true, IsVisible = false };
        await using var harness = UiHarness.Create(data.Open(), null, fake.Ports());
        var once = new StartOnce(harness.Shell.StartAsync);
        using var coordinator = new BackgroundCoordinator(harness.Services, once.RunAsync);

        fake.RequestResume();
        await coordinator.Resuming!;

        Assert.NotNull(harness.Services.Messenger);
        Assert.Equal([nameof(FakeBackground.StartRunning)], fake.Calls);
        Assert.True(once.HasStarted);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    [Trait("Story", "US-20")]
    public async Task Resume_PassphraseSet_AsksToUnlockAndStartsNothing()
    {
        using var data = new SavedData();
        await CreateSavedIdentityAsync(data, SavedData.Passphrase);
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true, IsVisible = false };
        await using var harness = UiHarness.Create(data.Open(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);

        fake.RequestResume();
        await coordinator.Resuming!;

        Assert.Null(harness.Services.Messenger);
        Assert.Equal(LocalDataStatus.PassphraseNeeded, harness.Services.OpenStatus);
        Assert.Equal([nameof(FakeBackground.ShowUnlockNeeded)], fake.Calls);
        Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Resume_NoIdentity_StopsTheService()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);

        fake.RequestResume();
        await coordinator.Resuming!;

        Assert.Null(harness.Services.Messenger);
        Assert.Equal([nameof(FakeBackground.StopRunning)], fake.Calls);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Quit_ConfirmedInTheNotification_StopsTheServiceAndTheMessengerAndEndsTheProcess()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var messenger = harness.Messenger;
        Assert.NotNull(messenger.Connections.ListeningPort);

        fake.RequestQuit();
        await coordinator.Quitting!;

        Assert.Equal([nameof(FakeBackground.StartRunning), nameof(FakeBackground.StopRunning), nameof(FakeBackground.EndProcess)], fake.Calls);
        Assert.Null(messenger.Connections.ListeningPort);
        Assert.False(fake.IsRunning);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Quit_InTheApp_AsksFirstAndQuitsOnlyWhenConfirmed()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var harness = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        using var coordinator = new BackgroundCoordinator(harness.Services, harness.Shell.StartAsync);
        await StartWithIdentityAsync(harness, "Alice");
        var page = harness.Shell.Pages.Background(false);
        var view = new BackgroundView { DataContext = page };
        harness.Show(view);
        var confirm = UiHarness.Find<Border>(view, "QuitConfirmPanel");
        Assert.False(confirm.IsVisible);

        UiHarness.Find<Button>(view, "QuitOpenButton").Command!.Execute(null);
        UiHarness.Flush();
        Assert.True(confirm.IsVisible);
        Assert.Equal(UiText.BackgroundQuitQuestion, UiHarness.Find<TextBlock>(view, "QuitQuestionText").Text);
        Assert.Equal(UiText.BackgroundQuitWarning, UiHarness.Find<TextBlock>(view, "QuitWarningText").Text);
        UiHarness.Find<Button>(view, "QuitCancelButton").Command!.Execute(null);
        UiHarness.Flush();
        Assert.False(confirm.IsVisible);
        Assert.DoesNotContain(nameof(FakeBackground.EndProcess), fake.Calls);

        page.OpenQuitCommand.Execute(null);
        await page.ConfirmQuitCommand.ExecuteAsync(null);

        Assert.Equal(nameof(FakeBackground.EndProcess), fake.Calls[^1]);
        Assert.Equal(nameof(FakeBackground.StopRunning), fake.Calls[^2]);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task Quit_Failing_ShowsTheErrorAndStaysOnThePage()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        var page = new BackgroundViewModel(fake.Ports(), () => throw new InvalidOperationException("no"), new NoNavigation(), false);

        page.OpenQuitCommand.Execute(null);
        await page.ConfirmQuitCommand.ExecuteAsync(null);

        Assert.Equal(UiText.BackgroundQuitFailed, page.ErrorMessage);
        Assert.False(page.IsQuitting);
        Assert.True(page.IsConfirmingQuit);
    }

    [AvaloniaFact]
    [Trait("Story", "US-18")]
    public async Task ContactList_BackgroundButton_ShowsOnlyWhereTheAppRunsInTheBackground()
    {
        var fake = new FakeBackground { IsExempt = true, IsAllowed = true };
        await using var withPorts = UiHarness.Create(new InMemoryLocalData(), null, fake.Ports());
        await StartWithIdentityAsync(withPorts, "Alice");
        var listView = new ContactListView { DataContext = withPorts.Shell.CurrentPage };
        withPorts.Show(listView);
        var button = UiHarness.Find<Button>(listView, "BackgroundButton");
        Assert.True(button.IsVisible);
        Assert.Equal(UiText.ContactListBackground, button.Content);
        button.Command!.Execute(null);
        Assert.IsType<BackgroundViewModel>(withPorts.Shell.CurrentPage);

        await using var desktop = await UiHarness.StartAsync("Bob");
        var desktopView = new ContactListView { DataContext = desktop.Shell.CurrentPage };
        desktop.Show(desktopView);
        Assert.False(UiHarness.Find<Button>(desktopView, "BackgroundButton").IsVisible);
    }

    private static async Task StartWithIdentityAsync(UiHarness harness, string name)
    {
        Assert.True((await harness.Services.OpenLocalDataAsync(CancellationToken.None)).IsOpen);
        Assert.True((await harness.Services.Identities.CreateAsync(name, CancellationToken.None)).IsCreated);
        await harness.Shell.StartAsync(CancellationToken.None);
        Assert.NotNull(harness.Services.Messenger);
        UiHarness.Flush();
    }

    private static Task WaitUntilAddedAsync(Messenger peer, Ed25519PublicKey contactKey) =>
        UiHarness.WaitUntilAsync(() => peer.Contacts.TryGet(contactKey, out Contact? contact) && contact.IsAdded && peer.Connections.IsConnected(contactKey));

    private static async Task CreateSavedIdentityAsync(SavedData data, string? passphrase)
    {
        await using var first = UiHarness.Create(data.Open());
        await first.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(first.Shell.CurrentPage);
        page.Name = "Alice";
        if (passphrase is not null)
        {
            page.Passphrase = passphrase;
            page.RepeatedPassphrase = passphrase;
        }

        await page.CreateCommand.ExecuteAsync(null);
        Assert.IsType<ContactListViewModel>(first.Shell.CurrentPage);
    }

    private sealed class NoNavigation : INavigationService
    {
        public PageViewModel? CurrentPage => null;

        public bool CanGoBack => false;

        public void Push(PageViewModel page)
        {
        }

        public void Replace(PageViewModel page)
        {
        }

        public void ResetTo(PageViewModel page)
        {
        }

        public void GoBack()
        {
        }
    }
}
