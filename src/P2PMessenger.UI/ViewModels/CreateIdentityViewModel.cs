using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class CreateIdentityViewModel : PageViewModel
{
    private readonly AppServices services;
    private readonly INavigationService navigation;
    private readonly Pages pages;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public CreateIdentityViewModel(AppServices services, INavigationService navigation, Pages pages)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);

        this.services = services;
        this.navigation = navigation;
        this.pages = pages;
    }

    public override string Title => UiText.CreateIdentityTitle;

    private bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            CreateIdentityResult result = await services.Identities.CreateAsync(Name, cancellationToken);
            if (!result.IsCreated)
            {
                ErrorMessage = result.Error == CreateIdentityError.AlreadyCreated
                    ? UiText.CreateIdentityAlreadyCreated
                    : UiText.CreateIdentityInvalidName;
                return;
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
}
