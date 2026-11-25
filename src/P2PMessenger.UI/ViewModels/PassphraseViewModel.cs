using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class PassphraseViewModel : PageViewModel
{
    private readonly AppServices services;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSet), nameof(IsNotSet), nameof(CanOfferRemove), nameof(StateText))]
    private bool hasPassphrase;

    [ObservableProperty]
    private string currentPassphrase = string.Empty;

    [ObservableProperty]
    private string newPassphrase = string.Empty;

    [ObservableProperty]
    private string repeatedPassphrase = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetCommand), nameof(ChangeCommand), nameof(RemoveCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public PassphraseViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        this.services = services;
        hasPassphrase = services.Protection == LocalDataProtection.Passphrase;
    }

    public override string Title => UiText.PassphraseTitle;

    public bool IsSet => HasPassphrase;

    public bool IsNotSet => !HasPassphrase;

    public bool CanOfferRemove => HasPassphrase && services.HasKeyStore;

    public string StateText => HasPassphrase
        ? Formats.With(Formats.PassphraseStateSet, (int)services.Lock.BackgroundLimit.TotalMinutes)
        : UiText.PassphraseStateNotSet;

    public static string Hint => PassphraseTexts.Hint;

    private bool IsIdle => !IsBusy;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SetAsync(CancellationToken cancellationToken) =>
        RunAsync(true, token => services.SetPassphraseAsync(NewPassphrase, token), UiText.PassphraseSetDone, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ChangeAsync(CancellationToken cancellationToken) =>
        RunAsync(true, token => services.ChangePassphraseAsync(CurrentPassphrase, NewPassphrase, token), UiText.PassphraseChanged, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RemoveAsync(CancellationToken cancellationToken) =>
        RunAsync(false, token => services.RemovePassphraseAsync(CurrentPassphrase, token), UiText.PassphraseRemoved, cancellationToken);

    private async Task RunAsync(bool needsNew, Func<CancellationToken, Task<PassphraseChangeResult>> change, string done, CancellationToken cancellationToken)
    {
        StatusMessage = null;
        ErrorMessage = needsNew ? PassphraseTexts.ProblemWith(NewPassphrase, RepeatedPassphrase) : null;
        if (ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            PassphraseChangeResult result = await change(cancellationToken);
            switch (result.Outcome)
            {
                case PassphraseChangeOutcome.Changed:
                    CurrentPassphrase = string.Empty;
                    NewPassphrase = string.Empty;
                    RepeatedPassphrase = string.Empty;
                    HasPassphrase = services.Protection == LocalDataProtection.Passphrase;
                    StatusMessage = done;
                    break;
                case PassphraseChangeOutcome.WrongPassphrase:
                    CurrentPassphrase = string.Empty;
                    ErrorMessage = UiText.PassphraseWrongCurrent;
                    break;
                case PassphraseChangeOutcome.NotAccepted:
                    ErrorMessage = PassphraseTexts.ProblemWith(NewPassphrase, NewPassphrase) ?? UiText.PassphraseFailed;
                    break;
                case PassphraseChangeOutcome.KeyStoreFailed:
                    ErrorMessage = UiText.PassphraseKeyStoreFailed;
                    break;
                default:
                    ErrorMessage = UiText.PassphraseFailed;
                    break;
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.PassphraseFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
