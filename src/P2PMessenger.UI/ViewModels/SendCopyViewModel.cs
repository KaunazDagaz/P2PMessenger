using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core;
using P2PMessenger.Core.Copy;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class SendCopyViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IQrScanner scanner;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private CancellationTokenSource? operationStop;
    private PcCopySender? sender;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CopySelectedCommand), nameof(DisconnectCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPairingVisible), nameof(IsSelectingVisible), nameof(HasNoCandidates))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CopySelectedCommand), nameof(DisconnectCommand))]
    private bool isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPairingVisible), nameof(IsSelectingVisible), nameof(HasNoCandidates))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CopySelectedCommand), nameof(DisconnectCommand))]
    private bool isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public SendCopyViewModel(Messenger messenger, IQrScanner scanner, IUiThread uiThread, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);

        this.messenger = messenger;
        this.scanner = scanner;
        this.uiThread = uiThread;
        this.navigation = navigation;
    }

    public static QrScanRequest ScanRequest { get; } = new(UiText.SendCopyScanTitle, UiText.SendCopyScanHint, UiText.ScanCancel, UiText.ScanStarting);

    public override string Title => UiText.SendCopyTitle;

    public ObservableCollection<CopyCandidateViewModel> Candidates { get; } = [];

    public bool IsPairingVisible => !IsConnected && !IsCompleted;

    public bool IsSelectingVisible => IsConnected && !IsCompleted;

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasNoCandidates => IsConnected && !IsCompleted && Candidates.Count == 0;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    private bool CanScan => !IsBusy && IsPairingVisible;

    private bool CanCopySelected => !IsBusy && IsSelectingVisible && HasCandidates;

    private bool CanDisconnect => !IsBusy && IsSelectingVisible;

    public override void OnHidden() => CloseActive();

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            QrScanResult result = await scanner.ScanAsync(ScanRequest, cancellationToken);
            switch (result.Status)
            {
                case QrScanStatus.Done when result.IsRead:
                    await ConnectFromTextAsync(result.Text, cancellationToken);
                    break;
                case QrScanStatus.Cancelled:
                    break;
                case QrScanStatus.NoCamera:
                    ErrorMessage = UiText.SendCopyNoCamera;
                    break;
                case QrScanStatus.CameraRefused:
                    ErrorMessage = UiText.SendCopyCameraRefused;
                    break;
                default:
                    ErrorMessage = UiText.SendCopyCameraFailed;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.SendCopyCameraFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private async Task CopySelectedAsync(CancellationToken cancellationToken)
    {
        if (sender is not { } active)
        {
            return;
        }

        List<CopyOriginId> selected = [.. Candidates.Where(static item => item.IsSelected).Select(static item => item.Origin)];
        if (selected.Count == 0)
        {
            ErrorMessage = UiText.SendCopyNothingSelected;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = Formats.WithAll(Formats.SendCopySending, 0, selected.Count);
        using CancellationTokenSource linked = BeginOperation(cancellationToken);
        try
        {
            PcCopySendResult result = await active.SendAsync(selected, new Progress<PcCopySendProgress>(OnSendProgress), linked.Token);
            if (result.Outcome == PcCopySendOutcome.NothingSelected)
            {
                ErrorMessage = UiText.SendCopyNothingSelected;
                StatusMessage = UiText.SendCopyConnected;
                return;
            }

            await DisposeSenderAsync();
            IsConnected = false;
            switch (result.Outcome)
            {
                case PcCopySendOutcome.Completed:
                    IsCompleted = true;
                    StatusMessage = result.CopiedCount == 1
                        ? UiText.SendCopyCompletedOne
                        : Formats.With(Formats.SendCopyCompleted, result.CopiedCount);
                    break;
                case PcCopySendOutcome.Disconnected:
                    StatusMessage = null;
                    ErrorMessage = UiText.SendCopyDisconnected;
                    break;
                case PcCopySendOutcome.Cancelled:
                    StatusMessage = null;
                    break;
                default:
                    StatusMessage = null;
                    ErrorMessage = UiText.SendCopyFailed;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await DisposeSenderAsync();
            IsConnected = false;
            StatusMessage = null;
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            await DisposeSenderAsync();
            IsConnected = false;
            StatusMessage = null;
            ErrorMessage = UiText.SendCopyFailed;
        }
        finally
        {
            EndOperation(linked);
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await DisposeSenderAsync();
            Candidates.Clear();
            OnPropertyChanged(nameof(HasCandidates));
            OnPropertyChanged(nameof(HasNoCandidates));
            IsConnected = false;
            StatusMessage = null;
            ErrorMessage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Done() => navigation.GoBack();

    private async Task ConnectFromTextAsync(string? text, CancellationToken cancellationToken)
    {
        if (!PcCopyService.TryParsePairingCode(text, out CopyPairingCode? pairingCode, out CopyPairingCodeError error))
        {
            ErrorMessage = error switch
            {
                CopyPairingCodeError.UnsupportedVersion => UiText.SendCopyUnsupportedVersion,
                CopyPairingCodeError.Damaged => UiText.SendCopyDamaged,
                _ => UiText.SendCopyNotACode,
            };
            return;
        }

        StatusMessage = UiText.SendCopyConnecting;
        using CancellationTokenSource linked = BeginOperation(cancellationToken);
        try
        {
            PcCopyConnectResult result = await messenger.PcCopy.ConnectAsync(pairingCode, linked.Token);
            if (!result.Connected)
            {
                StatusMessage = null;
                ErrorMessage = result.Outcome switch
                {
                    PcCopyConnectOutcome.TimedOut => UiText.SendCopyTimedOut,
                    PcCopyConnectOutcome.Refused => UiText.SendCopyRefused,
                    PcCopyConnectOutcome.Cancelled => null,
                    _ => UiText.SendCopyNotReachable,
                };
                return;
            }

            sender = result.Sender;
            LoadCandidates();
            IsConnected = true;
            StatusMessage = UiText.SendCopyConnected;
        }
        finally
        {
            EndOperation(linked);
        }
    }

    private void LoadCandidates()
    {
        Candidates.Clear();
        foreach (CopyCandidate candidate in messenger.PcCopy.ListCandidates())
        {
            Candidates.Add(new CopyCandidateViewModel(candidate, OnSelectionChanged));
        }

        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(HasNoCandidates));
        CopySelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged()
    {
        if (ErrorMessage == UiText.SendCopyNothingSelected && Candidates.Any(static item => item.IsSelected))
        {
            ErrorMessage = null;
        }
    }

    private void OnSendProgress(PcCopySendProgress progress) => uiThread.Post(() =>
    {
        if (IsBusy && IsConnected)
        {
            StatusMessage = Formats.WithAll(Formats.SendCopySending, progress.CompletedCount, progress.TotalCount);
        }
    });

    private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationStop = linked;
        return linked;
    }

    private void EndOperation(CancellationTokenSource linked)
    {
        if (ReferenceEquals(operationStop, linked))
        {
            operationStop = null;
        }
    }

    private void CloseActive()
    {
        CancellationTokenSource? stop = operationStop;
        operationStop = null;
        if (stop is not null)
        {
            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        PcCopySender? active = sender;
        sender = null;
        if (active is not null)
        {
            _ = DisposeQuietlyAsync(active);
        }
    }

    private async Task DisposeSenderAsync()
    {
        PcCopySender? active = sender;
        sender = null;
        if (active is not null)
        {
            await DisposeQuietlyAsync(active);
        }
    }

    private async Task DisposeQuietlyAsync(PcCopySender active)
    {
        try
        {
            await active.DisposeAsync();
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            ReportFailure(exception);
        }
    }
}
