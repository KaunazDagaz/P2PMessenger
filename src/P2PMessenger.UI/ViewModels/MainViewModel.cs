using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, INavigationService
{
    private readonly AppServices services;
    private readonly Stack<PageViewModel> pages = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private PageViewModel? currentPage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool canGoBack;

    [ObservableProperty]
    private bool isStarting = true;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? notice;

    public MainViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        Pages = new Pages(services, this);
        services.MessengerStarted += OnMessengerStarted;
    }

    public Pages Pages { get; }

    public string Title => CurrentPage?.Title ?? UiText.AppTitle;

    public bool HasNotice => Notice is not null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IsStarting = CurrentPage is null;
        ErrorMessage = null;
        try
        {
            LocalDataResult localData = await services.OpenLocalDataAsync(cancellationToken);
            if (!localData.IsOpen)
            {
                ResetTo(new StorageProblemViewModel(localData.Problem, StartAsync));
                return;
            }

            if (localData.Stores.Mode == LocalDataMode.MemoryOnlyNoKeyStore && Notice?.Contains(UiText.StorageMemoryOnly, StringComparison.Ordinal) != true)
            {
                Notice = Notice is null ? UiText.StorageMemoryOnly : UiText.StorageMemoryOnly + Environment.NewLine + Notice;
            }

            OwnIdentity? identity = await services.Identities.LoadAsync(cancellationToken);
            if (identity is null)
            {
                ResetTo(Pages.CreateIdentity());
                return;
            }

            await services.StartMessengerAsync(identity, cancellationToken);
            ResetTo(Pages.ContactList());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.AppStartFailed;
        }
        finally
        {
            IsStarting = false;
        }
    }

    public void Push(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        CurrentPage?.OnHidden();
        pages.Push(page);
        Show(page);
    }

    public void Replace(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (pages.TryPop(out PageViewModel? current))
        {
            current.OnHidden();
        }

        pages.Push(page);
        Show(page);
    }

    public void ResetTo(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        CurrentPage?.OnHidden();
        pages.Clear();
        pages.Push(page);
        Show(page);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void GoBack()
    {
        if (pages.Count < 2)
        {
            return;
        }

        pages.Pop().OnHidden();
        Show(pages.Peek());
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    private void OnMessengerStarted(object? sender, EventArgs e)
    {
        if (services.Messenger is { } messenger)
        {
            messenger.Contacts.Changed += OnContactChanged;
        }
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (e.Change != ContactChange.FirstConnection)
        {
            return;
        }

        string added = Formats.With(Formats.ContactAdded, e.Contact.Name.Value);
        services.UiThread.Post(() => Notice = Notice is null ? added : Notice + Environment.NewLine + added);
    }

    private void Show(PageViewModel page)
    {
        CurrentPage = page;
        CanGoBack = pages.Count > 1;
        page.OnShown();
    }
}
