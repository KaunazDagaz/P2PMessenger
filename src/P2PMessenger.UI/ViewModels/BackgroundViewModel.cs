using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Core;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class BackgroundViewModel : PageViewModel
{
    private readonly BackgroundPorts ports;
    private readonly Func<Task> quit;
    private readonly INavigationService navigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText), nameof(CanAskBattery))]
    private bool isExempt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText), nameof(CanAskBattery))]
    private bool isRestricted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsText), nameof(CanAskNotifications))]
    private bool notificationsAllowed;

    [ObservableProperty]
    private bool batteryRefused;

    [ObservableProperty]
    private bool showPreviews;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQuitOpenVisible))]
    private bool isConfirmingQuit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowBatteryCommand), nameof(AllowNotificationsCommand), nameof(OpenSettingsCommand), nameof(ConfirmQuitCommand), nameof(CancelQuitCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isQuitting;

    [ObservableProperty]
    private string? errorMessage;

    public BackgroundViewModel(BackgroundPorts ports, Func<Task> quit, INavigationService navigation, bool firstRun)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(quit);
        ArgumentNullException.ThrowIfNull(navigation);

        this.ports = ports;
        this.quit = quit;
        this.navigation = navigation;
        IsFirstRun = firstRun;
        if (firstRun)
        {
            ports.Settings.WasOffered = true;
        }

        showPreviews = ports.Settings.ShowPreviews;
        Refresh();
    }

    public override string Title => UiText.BackgroundTitle;

    public bool IsFirstRun { get; }

    public string BatteryText => IsRestricted ? UiText.BackgroundRestricted : IsExempt ? UiText.BackgroundBatteryExempt : UiText.BackgroundBatteryNotExempt;

    public bool CanAskBattery => !IsExempt && !IsRestricted;

    public string NotificationsText => NotificationsAllowed ? UiText.BackgroundNotificationsAllowed : UiText.BackgroundNotificationsOff;

    public bool CanAskNotifications => !NotificationsAllowed;

    public bool IsQuitOpenVisible => !IsConfirmingQuit;

    private bool IsIdle => !IsBusy;

    public override void OnShown() => Refresh();

    partial void OnShowPreviewsChanged(bool value) => ports.Settings.ShowPreviews = value;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AllowBatteryAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            bool exempt = await ports.Battery.RequestAsync(cancellationToken);
            IsExempt = exempt || ports.Battery.IsExempt;
            BatteryRefused = !IsExempt;
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            IsExempt = ports.Battery.IsExempt;
            BatteryRefused = !IsExempt;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenSettingsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            _ = await ports.Battery.OpenSettingsAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AllowNotificationsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            bool allowed = await ports.Notifier.RequestPermissionAsync(cancellationToken);
            NotificationsAllowed = allowed || ports.Notifier.IsAllowed;
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            NotificationsAllowed = ports.Notifier.IsAllowed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Continue() => navigation.GoBack();

    [RelayCommand]
    private void OpenQuit()
    {
        ErrorMessage = null;
        IsConfirmingQuit = true;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CancelQuit() => IsConfirmingQuit = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmQuitAsync()
    {
        IsBusy = true;
        IsQuitting = true;
        ErrorMessage = null;
        try
        {
            await quit();
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            IsQuitting = false;
            ErrorMessage = UiText.BackgroundQuitFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        IsRestricted = ports.Battery.IsRestricted;
        IsExempt = ports.Battery.IsExempt;
        NotificationsAllowed = ports.Notifier.IsAllowed;
        if (IsExempt || IsRestricted)
        {
            BatteryRefused = false;
        }
    }
}
