using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Identity;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Core;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, INavigationService
{
    private readonly AppServices services;
    private readonly Stack<PageViewModel> pages = new();
    private readonly List<PageViewModel> shown = [];

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
    private bool isWide;

    [ObservableProperty]
    private double keyboardInset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(IsBackVisible), nameof(CanLock), nameof(IsPageVisible))]
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
        FailureLog = services.FailureLog;
        Toasts = services.Toasts;
        Pages = new Pages(services, this, StartAsync, DeleteAllDataAsync);
        services.MessengerStarted += OnMessengerStarted;
        services.StoresOpened += OnStoresOpened;
        services.ProtectionChanged += OnProtectionChanged;
        services.Lock.Changed += OnLockChanged;
    }

    public Pages Pages { get; }

    public ObservableCollection<ToastViewModel> ToastItems => services.Toasts.Items;

    public string Title => IsLocked ? UiText.UnlockTitle : IsSplit ? DetailPage?.Title ?? string.Empty : CurrentPage?.Title ?? UiText.AppTitle;

    public bool IsLocked => LockPage is not null;

    public bool IsPageVisible => !IsLocked;

    public bool IsBackVisible => CanGoBack && !IsLocked;

    public bool CanLock => IsMessengerRunning && !IsLocked && services.Lock.IsEnabled;

    public PageViewModel? RootPage => pages.Count > 0 ? pages.Last() : null;

    public bool IsSplit => IsWide && !IsLocked && RootPage is ContactListViewModel;

    public PageViewModel? MasterPage => IsSplit ? RootPage : null;

    public PageViewModel? DetailPage => IsSplit && ReferenceEquals(CurrentPage, RootPage) ? null : CurrentPage;

    public bool IsDetailEmpty => IsSplit && DetailPage is null;

    public bool IsDetailBarVisible => !IsSplit || DetailPage is not null;

    public string MasterTitle => RootPage?.Title ?? string.Empty;

    public bool CanLockInBar => CanLock && !IsSplit;

    public bool CanLockInMaster => CanLock && IsSplit;

    public bool TryGoBack()
    {
        if (IsLocked || !CanGoBack)
        {
            return false;
        }

        GoBack();
        return true;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IsStarting = CurrentPage is null;
        ErrorMessage = null;
        try
        {
            await ShowAsync(await services.OpenLocalDataAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
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
        Toasts.Show(deleted ? ToastKind.Success : ToastKind.Warning, deleted ? UiText.DeleteAllDataDone : UiText.DeleteAllDataIncomplete);
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

    public void ShowUnexpectedError() => Toasts.Show(ToastKind.Error, UiText.AppUnexpectedError);

    public void Push(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        pages.Push(page);
        Show(page);
    }

    public void Open(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (IsWide && RootPage is ContactListViewModel)
        {
            while (pages.Count > 1)
            {
                _ = pages.Pop();
            }

            pages.Push(page);
            Show(page);
            return;
        }

        Push(page);
    }

    public void Replace(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        _ = pages.TryPop(out _);
        pages.Push(page);
        Show(page);
    }

    public void ResetTo(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

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

        _ = pages.Pop();
        Show(pages.Peek());
    }

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
                if (services.Background is { ShouldOffer: true })
                {
                    Push(Pages.Background(true));
                }

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

        services.UiThread.Post(() => Toasts.Show(ToastKind.Warning, UiText.StorageMemoryOnly, sticky: true));
    }

    private void OnProtectionChanged(object? sender, EventArgs e) => services.UiThread.Post(() =>
    {
        OnPropertyChanged(nameof(CanLock));
        OnPropertyChanged(nameof(CanLockInBar));
        OnPropertyChanged(nameof(CanLockInMaster));
        LockCommand.NotifyCanExecuteChanged();
    });

    private void OnLockChanged(object? sender, EventArgs e) => services.UiThread.Post(() =>
    {
        bool locked = services.Lock.IsLocked;
        if (locked == IsLocked)
        {
            return;
        }

        LockPage = locked ? Pages.Unlock(false, services.Lock.UnlockAsync) : null;
    });

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (e.Change != ContactChange.FirstConnection)
        {
            return;
        }

        string added = Formats.With(Formats.ContactAdded, e.Contact.Name.Value);
        services.UiThread.Post(() => Toasts.Show(ToastKind.Success, added));
    }

    partial void OnLockPageChanged(UnlockViewModel? value)
    {
        RefreshLayout();
        SyncShown();
    }

    partial void OnIsWideChanged(bool value)
    {
        RefreshLayout();
        SyncShown();
    }

    partial void OnIsMessengerRunningChanged(bool value) => RefreshLayout();

    private void Show(PageViewModel page)
    {
        CurrentPage = page;
        CanGoBack = pages.Count > 1;
        RefreshLayout();
        SyncShown();
    }

    private void RefreshLayout()
    {
        if (RootPage is ContactListViewModel list)
        {
            list.SetSelection(IsSplit && !ReferenceEquals(CurrentPage, list) ? CurrentPage : null);
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RootPage));
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(MasterPage));
        OnPropertyChanged(nameof(DetailPage));
        OnPropertyChanged(nameof(IsDetailEmpty));
        OnPropertyChanged(nameof(IsDetailBarVisible));
        OnPropertyChanged(nameof(MasterTitle));
        OnPropertyChanged(nameof(CanLockInBar));
        OnPropertyChanged(nameof(CanLockInMaster));
    }

    private void SyncShown()
    {
        List<PageViewModel> wanted = [];
        if (!IsLocked)
        {
            if (IsSplit && RootPage is { } root)
            {
                wanted.Add(root);
            }

            if (CurrentPage is { } current && !wanted.Contains(current))
            {
                wanted.Add(current);
            }
        }

        foreach (PageViewModel page in shown.Where(page => !wanted.Contains(page)).ToArray())
        {
            _ = shown.Remove(page);
            page.OnHidden();
        }

        foreach (PageViewModel page in wanted.Where(page => !shown.Contains(page)).ToArray())
        {
            shown.Add(page);
            page.OnShown();
        }
    }
}
