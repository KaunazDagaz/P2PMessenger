using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ReceiveCopyViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private CancellationTokenSource? sessionStop;
    private PcCopyReceiver? activeReceiver;
    private bool shown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCode))]
    private QrMatrix? qrCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string statusMessage = UiText.ReceiveCopyStarting;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool canRetry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowRetry), nameof(RetryButtonText))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool isCompleted;

    public ReceiveCopyViewModel(Messenger messenger, IUiThread uiThread, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);

        this.messenger = messenger;
        this.uiThread = uiThread;
        this.navigation = navigation;
    }

    public override string Title => UiText.ReceiveCopyTitle;

    public bool HasQrCode => QrCode is not null;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool CanShowRetry => CanRetry || IsCompleted;

    public string RetryButtonText => IsCompleted ? UiText.ReceiveCopyAnother : UiText.ReceiveCopyTryAgain;

    private bool CanStartAgain => !IsBusy && CanShowRetry;

    public override void OnShown()
    {
        shown = true;
        if (!IsCompleted && !CanRetry && sessionStop is null)
        {
            _ = RunSessionAsync();
        }
    }

    public override void OnHidden()
    {
        shown = false;
        StopSession();
    }

    [RelayCommand(CanExecute = nameof(CanStartAgain))]
    private void Retry()
    {
        if (!shown)
        {
            return;
        }

        StopSession();
        IsCompleted = false;
        CanRetry = false;
        _ = RunSessionAsync();
    }

    [RelayCommand]
    private void Done() => navigation.GoBack();

    private void StopSession()
    {
        CancellationTokenSource? stop = sessionStop;
        sessionStop = null;
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

        PcCopyReceiver? receiver = activeReceiver;
        activeReceiver = null;
        if (receiver is not null)
        {
            _ = DisposeQuietlyAsync(receiver);
        }
    }

    private async Task RunSessionAsync()
    {
        CancellationTokenSource stop = new();
        sessionStop = stop;
        CancellationToken token = stop.Token;

        IsBusy = true;
        ErrorMessage = null;
        CanRetry = false;
        IsCompleted = false;
        QrCode = null;
        StatusMessage = UiText.ReceiveCopyStarting;

        PcCopyReceiver? receiver = null;
        try
        {
            StartReceiveResult started = await messenger.PcCopy.StartReceivingAsync(token);
            if (token.IsCancellationRequested)
            {
                if (started.Started)
                {
                    await DisposeQuietlyAsync(started.Receiver);
                }

                return;
            }

            if (!started.Started)
            {
                IsBusy = false;
                CanRetry = true;
                StatusMessage = string.Empty;
                ErrorMessage = started.Problem == PcCopyReceiveProblem.NoLocalNetwork
                    ? UiText.ReceiveCopyNoNetwork
                    : UiText.ReceiveCopyListenFailed;
                return;
            }

            receiver = started.Receiver;
            activeReceiver = receiver;
            QrCode = QrMatrix.Encode(receiver.PairingCodeText);
            StatusMessage = UiText.ReceiveCopyWaiting;
            IsBusy = false;

            PcCopyReceiveResult result = await receiver.RunAsync(new Progress<PcCopyProgress>(OnProgress), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            QrCode = null;
            IsBusy = false;
            switch (result.Outcome)
            {
                case PcCopyReceivePhase.Completed:
                    IsCompleted = true;
                    StatusMessage = result.SavedCount == 1
                        ? UiText.ReceiveCopyCompletedOne
                        : Formats.With(Formats.ReceiveCopyCompleted, result.SavedCount);
                    break;
                case PcCopyReceivePhase.Disconnected:
                    CanRetry = true;
                    StatusMessage = string.Empty;
                    ErrorMessage = UiText.ReceiveCopyDisconnected;
                    break;
                case PcCopyReceivePhase.Cancelled:
                    break;
                default:
                    CanRetry = true;
                    StatusMessage = string.Empty;
                    ErrorMessage = UiText.ReceiveCopyFailed;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            if (!token.IsCancellationRequested)
            {
                QrCode = null;
                IsBusy = false;
                CanRetry = true;
                StatusMessage = string.Empty;
                ErrorMessage = UiText.ReceiveCopyFailed;
            }
        }
        finally
        {
            if (ReferenceEquals(activeReceiver, receiver))
            {
                activeReceiver = null;
            }

            if (receiver is not null)
            {
                await DisposeQuietlyAsync(receiver);
            }

            if (ReferenceEquals(sessionStop, stop))
            {
                sessionStop = null;
            }

            stop.Dispose();
        }
    }

    private void OnProgress(PcCopyProgress progress) => uiThread.Post(() =>
    {
        if (sessionStop is null)
        {
            return;
        }

        switch (progress.Phase)
        {
            case PcCopyReceivePhase.Connected:
                IsBusy = true;
                StatusMessage = UiText.ReceiveCopyConnected;
                break;
            case PcCopyReceivePhase.Receiving:
                IsBusy = true;
                StatusMessage = Formats.WithAll(Formats.ReceiveCopyReceiving, progress.CompletedCount, progress.TotalCount);
                break;
        }
    });

    private async Task DisposeQuietlyAsync(PcCopyReceiver receiver)
    {
        try
        {
            await receiver.DisposeAsync();
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            ReportFailure(exception);
        }
    }
}
