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
    [NotifyPropertyChangedFor(nameof(IsBackVisible))]
    private bool canGoBack;

    [ObservableProperty]
    private bool isStarting = true;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(Title), nameof(HasNotice), nameof(IsBackVisible), nameof(CanLock), nameof(IsPageVisible))]
    [NotifyCanExecuteChangedFor(nameof(LockCommand))]
    private UnlockViewModel? lockPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLock))]
    [NotifyCanExecuteChangedFor(nameof(LockCommand))]
    private bool isMessengerRunning;

    public MainViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        Pages = new Pages(services, this, StartAsync, DeleteAllDataAsync);
        services.MessengerStarted += OnMessengerStarted;
        services.StoresOpened += OnStoresOpened;
        services.ProtectionChanged += OnProtectionChanged;
        services.Lock.Changed += OnLockChanged;
    }

    public Pages Pages { get; }

    public string Title => IsLocked ? UiText.UnlockTitle : CurrentPage?.Title ?? UiText.AppTitle;

    public bool IsLocked => LockPage is not null;

    public bool IsPageVisible => !IsLocked;

    public bool HasNotice => Notice is not null && !IsLocked;

    public bool IsBackVisible => CanGoBack && !IsLocked;

    public bool CanLock => IsMessengerRunning && !IsLocked && services.Lock.IsEnabled;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IsStarting = CurrentPage is null;
        ErrorMessage = null;
        try
        {
            await ShowAsync(await services.OpenLocalDataAsync(cancellationToken), cancellationToken);
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

    public async Task<bool> DeleteAllDataAsync(CancellationToken cancellationToken)
    {
        bool deleted = await services.DeleteAllDataAsync(cancellationToken);
        LockPage = null;
        IsMessengerRunning = false;
        Notice = deleted ? UiText.DeleteAllDataDone : UiText.DeleteAllDataIncomplete;
        if (deleted)
        {
            ResetTo(Pages.CreateIdentity());
        }
        else
        {
            await StartAsync(cancellationToken);
        }

        return deleted;
    }

    public void EnterBackground() => services.Lock.EnterBackground();

    public void EnterForeground() => services.Lock.EnterForeground();

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

    [RelayCommand(CanExecute = nameof(CanLock))]
    private void Lock() => services.Lock.LockNow();

    private async Task ShowAsync(LocalDataResult localData, CancellationToken cancellationToken)
    {
        switch (localData.Status)
        {
            case LocalDataStatus.Opened:
                OwnIdentity? identity = await services.Identities.LoadAsync(cancellationToken);
                if (identity is null)
                {
                    ResetTo(Pages.CreateIdentity());
                    return;
                }

                await services.StartMessengerAsync(identity, cancellationToken);
                ResetTo(Pages.ContactList());
                break;
            case LocalDataStatus.NotCreated:
                ResetTo(Pages.CreateIdentity());
                break;
            case LocalDataStatus.PassphraseNeeded:
                ResetTo(Pages.Unlock(true, UnlockAtStartAsync));
                break;
            default:
                ResetTo(Pages.StorageProblem(localData.Problem == LocalDataProblem.None ? LocalDataProblem.Damaged : localData.Problem));
                break;
        }
    }

    private async Task<bool> UnlockAtStartAsync(string passphrase, CancellationToken cancellationToken)
    {
        LocalDataResult result = await services.UnlockLocalDataAsync(passphrase, cancellationToken);
        if (result.Status == LocalDataStatus.WrongPassphrase)
        {
            return false;
        }

        await ShowAsync(result, cancellationToken);
        return true;
    }

    private void OnMessengerStarted(object? sender, EventArgs e)
    {
        if (services.Messenger is { } messenger)
        {
            messenger.Contacts.Changed += OnContactChanged;
        }

        services.UiThread.Post(() => IsMessengerRunning = true);
    }

    private void OnStoresOpened(object? sender, LocalDataStores stores)
    {
        if (stores.Mode != LocalDataMode.MemoryOnlyNoKeyStore)
        {
            return;
        }

        services.UiThread.Post(() =>
        {
            if (Notice?.Contains(UiText.StorageMemoryOnly, StringComparison.Ordinal) != true)
            {
                Notice = Notice is null ? UiText.StorageMemoryOnly : UiText.StorageMemoryOnly + Environment.NewLine + Notice;
            }
        });
    }

    private void OnProtectionChanged(object? sender, EventArgs e) => services.UiThread.Post(() =>
    {
        OnPropertyChanged(nameof(CanLock));
        LockCommand.NotifyCanExecuteChanged();
    });

    private void OnLockChanged(object? sender, EventArgs e) => services.UiThread.Post(() =>
    {
        bool locked = services.Lock.IsLocked;
        if (locked == IsLocked)
        {
            return;
        }

        if (locked)
        {
            CurrentPage?.OnHidden();
            LockPage = Pages.Unlock(false, services.Lock.UnlockAsync);
        }
        else
        {
            LockPage = null;
            CurrentPage?.OnShown();
        }
    });

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
        if (!IsLocked)
        {
            page.OnShown();
        }
    }
}
