using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class MainViewTests
{
    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task Start_NoIdentity_ShowsIdentityCreationWithoutBackButton()
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(UiText.CreateIdentityTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        Assert.False(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        Assert.False(UiHarness.Find<TextBlock>(view, "StartingText").IsVisible);
        Assert.Null(harness.Services.Messenger);
    }

    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task CreateIdentity_ValidName_CreatesKeysStartsTheMessengerAndShowsContacts()
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        UiHarness.Flush();
        Assert.False(page.CreateCommand.CanExecute(null));

        page.Name = "Alice";
        Assert.True(page.CreateCommand.CanExecute(null));
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(UiText.ContactListTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        Assert.Equal("Alice", harness.Messenger.Identity.Name.Value);
        Assert.NotNull(harness.Messenger.Connections.ListeningPort);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaFact]
    [Trait("Story", "US-01")]
    public async Task CreateIdentity_NameWithAControlCharacter_ShowsTheErrorAndCreatesNothing()
    {
        await using var harness = UiHarness.Create();
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var view = new CreateIdentityView { DataContext = page };
        harness.Show(view);

        page.Name = "Ali\u0007ce";
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        var error = UiHarness.Find<TextBlock>(view, "ErrorText");
        Assert.True(error.IsVisible);
        Assert.Equal(UiText.CreateIdentityInvalidName, error.Text);
        Assert.Same(page, harness.Shell.CurrentPage);
        Assert.Null(harness.Services.Messenger);
    }

    [AvaloniaFact]
    public async Task Navigation_PushAndBack_ShowsTheBackButtonOnlyAboveTheFirstPage()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        var list = Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);

        list.MyCodeCommand.Execute(null);
        UiHarness.Flush();

        Assert.IsType<ProfileViewModel>(harness.Shell.CurrentPage);
        Assert.True(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        Assert.Equal(UiText.ProfileTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);

        harness.Shell.GoBackCommand.Execute(null);
        UiHarness.Flush();

        Assert.Same(list, harness.Shell.CurrentPage);
        Assert.False(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        harness.Messenger.Identity.Dispose();
    }

    [AvaloniaTheory]
    [Trait("Story", "US-02")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstConnection_OnAnyPage_SaysInOneLineThatTheKeyWasCheckedNotThePerson(bool onAnotherPage)
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        if (onAnotherPage)
        {
            Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).MyCodeCommand.Execute(null);
        }

        var bob = await harness.ConnectPeerAsync("Bob");
        await UiHarness.WaitUntilAsync(() => UiHarness.Find<Border>(view, "NoticePanel").IsVisible);

        Assert.Equal("Bob is added. The app checked their key, not who they are.", UiHarness.Find<TextBlock>(view, "NoticeText").Text);
        Assert.DoesNotContain(UiHarness.Texts(view), static text => text.Contains("verified", StringComparison.OrdinalIgnoreCase));
        harness.Shell.DismissNoticeCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(UiHarness.Find<Border>(view, "NoticePanel").IsVisible);
        await UiHarness.StopPeerAsync(bob);
        harness.Messenger.Identity.Dispose();
    }
}
