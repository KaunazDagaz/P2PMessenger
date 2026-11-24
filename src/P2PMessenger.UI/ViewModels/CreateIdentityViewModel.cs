using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class CreateIdentityViewModel : PageViewModel
{
    private readonly AppServices services;
    private readonly INavigationService navigation;
    private readonly Pages pages;
    private readonly Func<LocalDataProblem, PageViewModel> problemPage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkippingPassphrase))]
    private string passphrase = string.Empty;

    [ObservableProperty]
    private string repeatedPassphrase = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool useTor = true;

    public CreateIdentityViewModel(AppServices services, INavigationService navigation, Pages pages, Func<LocalDataProblem, PageViewModel> problemPage)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(problemPage);

        this.services = services;
        this.navigation = navigation;
        this.pages = pages;
        this.problemPage = problemPage;
        OffersPassphrase = services.SupportsPassphrase && services.Protection != LocalDataProtection.Passphrase && services.Mode is null or LocalDataMode.Saved;
        OffersTor = services.OffersTor;
    }

    public override string Title => UiText.CreateIdentityTitle;

    public bool OffersPassphrase { get; }

    public bool OffersTor { get; }

    public bool IsSkippingPassphrase => OffersPassphrase && string.IsNullOrEmpty(Passphrase);

    public string SkipWarning => services.HasKeyStore ? UiText.CreateIdentityNoPassphraseWarning : UiText.CreateIdentityNoPassphraseNoKeyStore;

    public static string Hint => PassphraseTexts.Hint;

    private bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        if (!DisplayName.TryCreate(Name, out _))
        {
            ErrorMessage = UiText.CreateIdentityInvalidName;
            return;
        }

        string? chosen = OffersPassphrase && !string.IsNullOrEmpty(Passphrase) ? Passphrase : null;
        if (chosen is not null && PassphraseTexts.ProblemWith(chosen, RepeatedPassphrase) is { } problem)
        {
            ErrorMessage = problem;
            return;
        }

        IsBusy = true;
        try
        {
            if (!await PrepareLocalDataAsync(chosen, cancellationToken))
            {
                return;
            }

            CreateIdentityResult result = await services.Identities.CreateAsync(Name, cancellationToken);
            if (!result.IsCreated)
            {
                ErrorMessage = result.Error == CreateIdentityError.AlreadyCreated
                    ? UiText.CreateIdentityAlreadyCreated
                    : UiText.CreateIdentityInvalidName;
                return;
            }

            if (OffersTor)
            {
                services.TorSettings.UseTor = UseTor;
            }

            await services.StartMessengerAsync(result.Identity, cancellationToken);
            navigation.ResetTo(pages.ContactList());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.CreateIdentityFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> PrepareLocalDataAsync(string? chosen, CancellationToken cancellationToken)
    {
        if (services.HasStores)
        {
            if (chosen is null || services.Protection != LocalDataProtection.KeyStore)
            {
                return true;
            }

            PassphraseChangeResult changed = await services.SetPassphraseAsync(chosen, cancellationToken);
            if (changed.IsChanged)
            {
                return true;
            }

            ErrorMessage = changed.Outcome == PassphraseChangeOutcome.KeyStoreFailed ? UiText.PassphraseKeyStoreFailed : UiText.CreateIdentityFailed;
            return false;
        }

        LocalDataResult created = await services.CreateLocalDataAsync(chosen, cancellationToken);
        switch (created.Status)
        {
            case LocalDataStatus.Opened:
                return true;
            case LocalDataStatus.PassphraseRequired:
                ErrorMessage = UiText.CreateIdentityPassphraseRequired;
                return false;
            case LocalDataStatus.Failed:
                navigation.ResetTo(problemPage(created.Problem));
                return false;
            default:
                ErrorMessage = UiText.CreateIdentityFailed;
                return false;
        }
    }
}
