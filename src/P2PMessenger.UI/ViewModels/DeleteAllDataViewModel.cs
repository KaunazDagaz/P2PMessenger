using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Core;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class DeleteAllDataViewModel : ViewModelBase
{
    private readonly Func<CancellationToken, Task<bool>> deleteAll;
    private readonly Action? cancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenVisible))]
    private bool isConfirming;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public DeleteAllDataViewModel(Func<CancellationToken, Task<bool>> deleteAll, string? intro)
        : this(deleteAll, intro, false, null)
    {
    }

    public DeleteAllDataViewModel(Func<CancellationToken, Task<bool>> deleteAll, string? intro, bool confirming, Action? cancelled)
    {
        ArgumentNullException.ThrowIfNull(deleteAll);

        this.deleteAll = deleteAll;
        this.cancelled = cancelled;
        Intro = intro;
        isConfirming = confirming;
    }

    public string? Intro { get; }

    public bool HasIntro => Intro is not null;

    public bool IsOpenVisible => !IsConfirming;

    private bool IsIdle => !IsBusy;

    [RelayCommand]
    private void Open()
    {
        ErrorMessage = null;
        IsConfirming = true;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Cancel()
    {
        ErrorMessage = null;
        if (cancelled is not null)
        {
            cancelled();
            return;
        }

        IsConfirming = false;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await deleteAll(cancellationToken);
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.DeleteAllDataFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
