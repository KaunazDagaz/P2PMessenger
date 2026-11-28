using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.ViewModels;
using P2PMessenger.UI.Views;

namespace P2PMessenger.UI.Tests.Views;

public sealed class StorageProblemViewTests
{
    public static TheoryData<LocalDataProblem, string> Problems { get; } = new()
    {
        { LocalDataProblem.KeyStoreUnavailable, UiText.StorageKeyStoreUnavailable },
        { LocalDataProblem.KeyStoreRefused, UiText.StorageKeyStoreRefused },
        { LocalDataProblem.KeyNotFound, UiText.StorageKeyNotFound },
        { LocalDataProblem.NewerVersion, UiText.StorageNewerVersion },
        { LocalDataProblem.Damaged, UiText.StorageDamaged },
    };

    [AvaloniaTheory]
    [Trait("Story", "US-20")]
    [MemberData(nameof(Problems))]
    public async Task Start_StoredDataCannotBeOpened_ShowsWhyAndCreatesNothing(LocalDataProblem problem, string message)
    {
        await using var harness = UiHarness.Create(new FakeLocalData(problem));
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        var page = Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(problem, page.Problem);
        Assert.Equal(UiText.StorageProblemTitle, UiHarness.Find<TextBlock>(view, "TitleText").Text);
        Assert.Contains(message, UiHarness.Texts(view));
        Assert.False(UiHarness.Find<Button>(view, "BackButton").IsVisible);
        Assert.Null(harness.Services.Messenger);
        Assert.Throws<InvalidOperationException>(() => harness.Services.Identities);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task TryAgain_ProblemSolved_GoesOnToIdentityCreation()
    {
        var localData = new FakeLocalData(LocalDataProblem.KeyStoreRefused);
        await using var harness = UiHarness.Create(localData);
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);
        await harness.Shell.StartAsync(CancellationToken.None);
        var page = Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);

        await page.TryAgainCommand.ExecuteAsync(null);
        UiHarness.Flush();
        Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage);

        localData.Problem = LocalDataProblem.None;
        await Assert.IsType<StorageProblemViewModel>(harness.Shell.CurrentPage).TryAgainCommand.ExecuteAsync(null);
        UiHarness.Flush();

        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        Assert.Equal(3, localData.Opened);
        Assert.Empty(harness.Services.Toasts.Items);
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task ProblemPage_ShowsItsMessageAndAButtonToTryAgain()
    {
        var page = new StorageProblemViewModel(LocalDataProblem.KeyNotFound, _ => Task.CompletedTask);
        await using var harness = UiHarness.Create();
        var view = new StorageProblemView { DataContext = page };
        harness.Show(view);

        Assert.Equal(UiText.StorageKeyNotFound, UiHarness.Find<TextBlock>(view, "MessageText").Text);
        Assert.Equal(UiText.StorageTryAgain, UiHarness.Find<Button>(view, "TryAgainButton").Content);
        Assert.False(UiHarness.Find<TextBlock>(view, "BusyText").IsVisible);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageProblemViewModel(LocalDataProblem.None, _ => Task.CompletedTask));
    }

    [AvaloniaFact]
    [Trait("Story", "US-20")]
    public async Task Start_NoKeyStore_SaysOnceThatNothingIsSaved()
    {
        await using var harness = UiHarness.Create(new InMemoryLocalData(LocalDataMode.MemoryOnlyNoKeyStore));
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);

        await harness.Shell.StartAsync(CancellationToken.None);
        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        Assert.IsType<CreateIdentityViewModel>(harness.Shell.CurrentPage);
        var toast = Assert.Single(harness.Services.Toasts.Items);
        Assert.Equal(UiText.StorageMemoryOnly, toast.Text);
        Assert.True(toast.IsSticky);
        Assert.Contains(UiText.StorageMemoryOnly, UiHarness.Texts(view));
    }

    [AvaloniaFact]
    public async Task Start_DataInMemoryForTests_ShowsNoNotice()
    {
        await using var harness = UiHarness.Create();
        var view = new MainView { DataContext = harness.Shell };
        harness.Show(view);

        await harness.Shell.StartAsync(CancellationToken.None);
        UiHarness.Flush();

        Assert.Empty(harness.Services.Toasts.Items);
    }
}
