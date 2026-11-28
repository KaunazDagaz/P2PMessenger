using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class PassphraseViewTests
{
    private const string OtherPassphrase = "violet harbour 4471 kite";

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_SavedData_OffersThePassphraseAndWarnsInOneLineWhileItIsEmpty()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var view = new CreateIdentityView { DataContext = page };
        harness.Show(view);

        Assert.True(UiHarness.Find<StackPanel>(view, "PassphrasePanel").IsVisible);
        Assert.Equal('•', UiHarness.Find<TextBox>(view, "PassphraseBox").PasswordChar);
        Assert.Equal("At least 12 characters.", UiHarness.Find<TextBlock>(view, "HintText").Text);
        var warning = UiHarness.Find<TextBlock>(view, "SkipWarningText");
        Assert.True(warning.IsVisible);
        Assert.Equal(UiText.CreateIdentityNoPassphraseWarning, warning.Text);

        page.Passphrase = SavedData.Passphrase;
        UiHarness.Flush();

        Assert.False(warning.IsVisible);
        Assert.False(File.Exists(data.DatabasePath));
    }

    [AvaloniaTheory]
    [Trait("Story", "US-20")]
    [InlineData("eleven char", "eleven char", "The passphrase needs at least 12 characters.")]
    [InlineData(SavedData.Passphrase, "lantern orchard pebbles", "The two passphrases are not the same.")]
    public async Task CreateIdentity_PassphraseNotAccepted_SaysWhyAndCreatesNothing(string passphrase, string repeated, string expected)
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Alice";
        page.Passphrase = passphrase;
        page.RepeatedPassphrase = repeated;
        await page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(expected, page.ErrorMessage);
        Assert.Null(harness.Services.Messenger);
        Assert.False(File.Exists(data.DatabasePath));
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_WithPassphrase_SavesUnderItAndShowsTheLockButton()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Alice";
        page.Passphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(LocalDataProtection.Passphrase, harness.Services.Protection);
        Assert.True(UiHarness.Find<Button>(view, "LockButton").IsVisible);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(UiHarness.Descendant<Button>(view, "PassphraseButton").IsVisible);
        Assert.DoesNotContain(UiText.StorageMemoryOnly, harness.ToastTexts);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_WithoutPassphrase_SavesUnderTheKeyStoreWithoutALockButton()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Alice";
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(LocalDataProtection.KeyStore, harness.Services.Protection);
        Assert.False(UiHarness.Find<Button>(view, "LockButton").IsVisible);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Assert.True(UiHarness.Descendant<Button>(view, "PassphraseButton").IsVisible);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_NoKeyStoreAnswers_AsksForAPassphraseAndWritesNothing()
    {
        using var data = new SavedData();
        data.Store.Outcome = KeyStoreOutcome.Unavailable;
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Alice";
        await page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.CreateIdentityPassphraseRequired, page.ErrorMessage);
        Assert.Null(harness.Services.Messenger);
        Assert.False(Directory.Exists(data.Folder));

        page.Passphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.CreateCommand.ExecuteAsync(null);

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(LocalDataProtection.Passphrase, harness.Services.Protection);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_NoKeyStoreOnThisDevice_WarnsThatNothingIsSavedAndSaysSoAfterwards()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open(withKeyStore: false));
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        UiHarness.Flush();
        Assert.Equal(UiText.CreateIdentityNoPassphraseNoKeyStore, page.SkipWarning);
        Assert.DoesNotContain(UiText.StorageMemoryOnly, harness.ToastTexts);

        page.Name = "Alice";
        await page.CreateCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Contains(UiText.StorageMemoryOnly, harness.ToastTexts);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).OpenMenuCommand.Execute(null);
        UiHarness.Flush();
        Assert.False(UiHarness.Descendant<Button>(view, "PassphraseButton").IsVisible);
        Assert.False(Directory.Exists(data.Folder));
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_SavedDataWithoutAnIdentity_PutsTheExistingKeyUnderThePassphrase()
    {
        using var data = new SavedData();
        await using (var earlier = data.Open())
        {
            Assert.True((await earlier.CreateAsync(null, CancellationToken.None)).IsOpen);
        }

        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.True(page.OffersPassphrase);
        Assert.Equal(LocalDataProtection.KeyStore, harness.Services.Protection);

        page.Name = "Alice";
        page.Passphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.CreateCommand.ExecuteAsync(null);

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(LocalDataProtection.Passphrase, harness.Services.Protection);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_SavedDataWithoutAnIdentityAndARefusingKeyStore_SaysSoAndCreatesNothing()
    {
        using var data = new SavedData();
        await using (var earlier = data.Open())
        {
            Assert.True((await earlier.CreateAsync(null, CancellationToken.None)).IsOpen);
        }

        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        data.Store.Outcome = KeyStoreOutcome.Refused;

        page.Name = "Alice";
        page.Passphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.PassphraseKeyStoreFailed, page.ErrorMessage);
        Assert.Null(harness.Services.Messenger);
        Assert.Equal(LocalDataProtection.KeyStore, harness.Services.Protection);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_KeyStoreRefusesANewKey_ShowsTheStoredDataProblem()
    {
        using var data = new SavedData();
        data.Store.Outcome = KeyStoreOutcome.Refused;
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Alice";
        await page.CreateCommand.ExecuteAsync(null);

        var problem = Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(LocalDataProblem.KeyStoreRefused, problem.Problem);
        Assert.False(Directory.Exists(data.Folder));

        data.Store.Outcome = KeyStoreOutcome.Succeeded;
        await problem.TryAgainCommand.ExecuteAsync(null);
        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task CreateIdentity_InvalidNameWithAPassphrase_WritesNothing()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);

        page.Name = "Ali\u0007ce";
        page.Passphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(UiText.CreateIdentityInvalidName, page.ErrorMessage);
        Assert.False(Directory.Exists(data.Folder));
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task Start_DataUnderAPassphrase_AsksForItAndStartsNothingUntilItIsRight()
    {
        using var data = new SavedData();
        await CreateAsync(data, SavedData.Passphrase);
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        var page = Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
        Assert.Null(harness.Services.Messenger);
        Assert.Equal(UiText.UnlockTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        Assert.False(UiHarness.Find<Button>(view, "LockButton").IsVisible);
        Assert.False(page.UnlockCommand.CanExecute(null));
        Assert.Equal(UiText.PassphraseForgotten, UiHarness.Descendant<TextBlock>(UiHarness.Find<ContentControl>(view, "PageHost"), "ForgottenText").Text);

        page.Passphrase = OtherPassphrase;
        await page.UnlockCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Same(page, harness.Shell.CurrentPage);
        Assert.Equal(UiText.UnlockWrong, UiHarness.Descendant<TextBlock>(UiHarness.Find<ContentControl>(view, "PageHost"), "ErrorText").Text);
        Assert.Equal(string.Empty, page.Passphrase);
        Assert.Null(harness.Services.Messenger);

        page.Passphrase = SavedData.Passphrase;
        await page.UnlockCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage);
        Assert.Equal("Alice", harness.Messenger.Identity.Name.Value);
        Assert.True(UiHarness.Find<Button>(view, "LockButton").IsVisible);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task Lock_HidesThePageTitleBackButtonAndNoticeUntilThePassphraseIsEntered()
    {
        using var data = new SavedData();
        await CreateAsync(data, SavedData.Passphrase);
        await using var harness = UiHarness.Create(data.Open());
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var unlock = Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
        unlock.Passphrase = SavedData.Passphrase;
        await unlock.UnlockCommand.ExecuteAsync(null);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).MyCodeCommand.Execute(null);
        harness.Services.Toasts.Show(ToastKind.Success, "Bob is added.");
        UiHarness.Flush();
        var profile = Assert.IsType<ProfileViewModel>(harness.Shell.CurrentPage);

        harness.Shell.LockCommand.Execute(null);
        UiHarness.Flush();

        Assert.True(harness.Shell.IsLocked);
        Assert.False(UiHarness.Find<ContentControl>(view, "PageHost").IsVisible);
        Assert.True(UiHarness.Find<ContentControl>(view, "LockHost").IsVisible);
        Assert.Equal(UiText.UnlockTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        Assert.False(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "LockButton").IsVisible);
        Assert.False(UiHarness.Find<ItemsControl>(view, "ToastHost").IsVisible);
        Assert.DoesNotContain(harness.Messenger.Identity.CodeText, UiHarness.Texts(view));

        var page = harness.Shell.LockPage!;
        page.Passphrase = OtherPassphrase;
        await page.UnlockCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.True(harness.Shell.IsLocked);
        Assert.Equal(UiText.UnlockWrong, page.ErrorMessage);

        page.Passphrase = SavedData.Passphrase;
        await page.UnlockCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.False(harness.Shell.IsLocked);
        Assert.Same(profile, harness.Shell.CurrentPage);
        Assert.True(UiHarness.Find<ContentControl>(view, "PageHost").IsVisible);
        Assert.True(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        Assert.True(UiHarness.Find<ItemsControl>(view, "ToastHost").IsVisible);
        Assert.Contains("Bob is added.", UiHarness.Texts(view));
        Assert.Equal(UiText.ProfileTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task Background_BrieflyAndBack_DoesNotLock()
    {
        using var data = new SavedData();
        await CreateAsync(data, SavedData.Passphrase);
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var unlock = Assert.IsType<UnlockViewModel>(harness.Shell.CurrentPage);
        unlock.Passphrase = SavedData.Passphrase;
        await unlock.UnlockCommand.ExecuteAsync(null);

        harness.Shell.EnterBackground();
        Assert.True(harness.Services.Lock.IsInBackground);
        harness.Shell.EnterForeground();

        Assert.False(harness.Services.Lock.IsInBackground);
        Assert.False(harness.Shell.IsLocked);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task PassphrasePage_SetChangeAndRemove_FollowTheCurrentPassphrase()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        var main = new MainView { DataContext = harness.Shell };
        harness.Show(main);
        await harness.Shell.StartAsync(CancellationToken.None);
        var create = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        create.Name = "Alice";
        await create.CreateCommand.ExecuteAsync(null);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).PassphraseCommand.Execute(null);
        var page = Assert.IsType<PassphraseViewModel>(harness.Shell.CurrentPage);
        var view = new PassphraseView { DataContext = page };
        harness.Show(view);

        Assert.Equal(UiText.PassphraseStateNotSet, UiHarness.Find<TextBlock>(view, "StateText").Text);
        Assert.True(UiHarness.Find<Button>(view, "SetButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "ChangeButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(view, "RemoveButton").IsEffectivelyVisible);
        Assert.False(UiHarness.Find<TextBlock>(view, "ForgottenText").IsVisible);

        page.NewPassphrase = "short";
        page.RepeatedPassphrase = "short";
        await page.SetCommand.ExecuteAsync(null);
        Assert.Equal("The passphrase needs at least 12 characters.", page.ErrorMessage);

        page.NewPassphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.SetCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Contains(UiText.PassphraseSetDone, harness.ToastTexts);
        Assert.Equal(LocalDataProtection.Passphrase, harness.Services.Protection);
        Assert.Equal(UiText.PassphraseStateSet, UiHarness.Find<TextBlock>(view, "StateText").Text);
        Assert.True(UiHarness.Find<Button>(view, "ChangeButton").IsVisible);
        Assert.True(UiHarness.Find<Button>(view, "RemoveButton").IsEffectivelyVisible);
        Assert.Equal(UiText.PassphraseForgotten, UiHarness.Find<TextBlock>(view, "ForgottenText").Text);
        Assert.True(UiHarness.Find<TextBlock>(view, "ForgottenText").IsVisible);
        Assert.True(UiHarness.Find<Button>(main, "LockButton").IsVisible);
        Assert.Equal(string.Empty, page.NewPassphrase);

        page.CurrentPassphrase = OtherPassphrase;
        page.NewPassphrase = OtherPassphrase;
        page.RepeatedPassphrase = OtherPassphrase;
        await page.ChangeCommand.ExecuteAsync(null);
        Assert.Equal(UiText.PassphraseWrongCurrent, page.ErrorMessage);
        Assert.Equal(string.Empty, page.CurrentPassphrase);

        page.CurrentPassphrase = SavedData.Passphrase;
        await page.ChangeCommand.ExecuteAsync(null);
        Assert.Contains(UiText.PassphraseChanged, harness.ToastTexts);
        Assert.Null(page.ErrorMessage);

        page.CurrentPassphrase = OtherPassphrase;
        await page.RemoveCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.Contains(UiText.PassphraseRemoved, harness.ToastTexts);
        Assert.Equal(LocalDataProtection.KeyStore, harness.Services.Protection);
        Assert.True(UiHarness.Find<Button>(view, "SetButton").IsVisible);
        Assert.False(UiHarness.Find<Button>(main, "LockButton").IsVisible);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task PassphraseSet_IsReportedWheneverItChanges()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        var reported = new List<bool>();
        harness.Services.PassphraseSetChanged += (_, set) => reported.Add(set);
        await harness.Shell.StartAsync(CancellationToken.None);
        var create = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        create.Name = "Alice";
        create.Passphrase = SavedData.Passphrase;
        create.RepeatedPassphrase = SavedData.Passphrase;
        await create.CreateCommand.ExecuteAsync(null);
        var page = harness.Shell.Pages.Passphrase();

        page.CurrentPassphrase = SavedData.Passphrase;
        await page.RemoveCommand.ExecuteAsync(null);
        page.NewPassphrase = OtherPassphrase;
        page.RepeatedPassphrase = OtherPassphrase;
        await page.SetCommand.ExecuteAsync(null);
        page.CurrentPassphrase = OtherPassphrase;
        await page.RemoveCommand.ExecuteAsync(null);

        Assert.Equal([true, false, true, false], reported);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task PassphrasePage_KeyStoreRefuses_SaysSoAndChangesNothing()
    {
        using var data = new SavedData();
        await using var harness = UiHarness.Create(data.Open());
        await harness.Shell.StartAsync(CancellationToken.None);
        var create = Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        create.Name = "Alice";
        await create.CreateCommand.ExecuteAsync(null);
        var page = harness.Shell.Pages.Passphrase();
        data.Store.Outcome = KeyStoreOutcome.Refused;

        page.NewPassphrase = SavedData.Passphrase;
        page.RepeatedPassphrase = SavedData.Passphrase;
        await page.SetCommand.ExecuteAsync(null);

        Assert.Equal(UiText.PassphraseKeyStoreFailed, page.ErrorMessage);
        Assert.Equal(LocalDataProtection.KeyStore, harness.Services.Protection);
        Assert.False(page.IsSet);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task ContactList_DataInMemory_OffersNoPassphrase()
    {
        await using var harness = await UiHarness.StartAsync("Alice");
        var view = new ContactListView { DataContext = harness.Shell.CurrentPage };
        harness.Show(view);
        Assert.IsType<ContactListViewModel>(harness.Shell.CurrentPage).OpenMenuCommand.Execute(null);
        UiHarness.Flush();

        Assert.False(UiHarness.Descendant<Button>(view, "PassphraseButton").IsVisible);
        Assert.False(harness.Shell.CanLock);
        harness.Messenger.Identity.Dispose();
    }

    private static async Task CreateAsync(SavedData data, string passphrase)
    {
        await using var localData = data.Open();
        var created = await localData.CreateAsync(passphrase, CancellationToken.None);
        Assert.True(created.IsOpen);
        using var identity = (await new Application.Identity.IdentityService(created.Stores.Identity).CreateAsync("Alice", CancellationToken.None)).Identity;
        Assert.NotNull(identity);
    }
}
