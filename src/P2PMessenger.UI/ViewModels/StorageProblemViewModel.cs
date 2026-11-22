using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class StorageProblemViewModel : PageViewModel
{
    private readonly Func<CancellationToken, Task> tryAgain;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TryAgainCommand))]
    private bool isBusy;

    public StorageProblemViewModel(LocalDataProblem problem, Func<CancellationToken, Task> tryAgain)
        : this(problem, tryAgain, null)
    {
    }

    public StorageProblemViewModel(LocalDataProblem problem, Func<CancellationToken, Task> tryAgain, DeleteAllDataViewModel? deleteAll)
    {
        ArgumentNullException.ThrowIfNull(tryAgain);
        if (problem == LocalDataProblem.None || !Enum.IsDefined(problem))
        {
            throw new ArgumentOutOfRangeException(nameof(problem), problem, "The page needs a problem to show.");
        }

        Problem = problem;
        Message = MessageOf(problem);
        this.tryAgain = tryAgain;
        DeleteAll = deleteAll;
    }

    public LocalDataProblem Problem { get; }

    public DeleteAllDataViewModel? DeleteAll { get; }

    public bool OffersDeleteAll => DeleteAll is not null;

    public string Message { get; }

    public override string Title => UiText.StorageProblemTitle;

    private bool CanTryAgain => !IsBusy;

    private static string MessageOf(LocalDataProblem problem) => problem switch
    {
        LocalDataProblem.KeyStoreUnavailable => UiText.StorageKeyStoreUnavailable,
        LocalDataProblem.KeyStoreRefused => UiText.StorageKeyStoreRefused,
        LocalDataProblem.KeyNotFound => UiText.StorageKeyNotFound,
        LocalDataProblem.NewerVersion => UiText.StorageNewerVersion,
        _ => UiText.StorageDamaged,
    };

    [RelayCommand(CanExecute = nameof(CanTryAgain))]
    private async Task TryAgainAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await tryAgain(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
