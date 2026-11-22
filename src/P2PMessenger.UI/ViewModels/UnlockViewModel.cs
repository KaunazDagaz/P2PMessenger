using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class UnlockViewModel : PageViewModel
{
    private readonly Func<string, CancellationToken, Task<bool>> unlock;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string passphrase = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public UnlockViewModel(bool atStart, Func<string, CancellationToken, Task<bool>> unlock)
    {
        ArgumentNullException.ThrowIfNull(unlock);

        AtStart = atStart;
        this.unlock = unlock;
    }

    public bool AtStart { get; }

    public string Intro => AtStart ? UiText.UnlockStartIntro : UiText.UnlockLockedIntro;

    public override string Title => UiText.UnlockTitle;

    private bool CanUnlock => !IsBusy && !string.IsNullOrEmpty(Passphrase);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!await unlock(Passphrase, cancellationToken))
            {
                ErrorMessage = UiText.UnlockWrong;
            }

            Passphrase = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.UnlockFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
