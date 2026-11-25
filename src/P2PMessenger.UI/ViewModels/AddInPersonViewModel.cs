using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core;
using P2PMessenger.Core.Crypto;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class AddInPersonViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IQrScanner scanner;
    private readonly IUiThread uiThread;
    private readonly INavigationService navigation;
    private readonly Pages pages;
    private CodeCheck? pending;
    private Ed25519PublicKey? scannedKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(AddCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    [ObservableProperty]
    private bool offersTextCodes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? previewName;

    [ObservableProperty]
    private string? previewFingerprint;

    [ObservableProperty]
    private string? previewHint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string? addText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScannedContact))]
    private string? scannedFingerprint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenChatCommand))]
    private bool isConnected;

    public AddInPersonViewModel(Messenger messenger, IQrScanner scanner, IUiThread uiThread, INavigationService navigation, Pages pages)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);

        this.messenger = messenger;
        this.scanner = scanner;
        this.uiThread = uiThread;
        this.navigation = navigation;
        this.pages = pages;
        QrCode = QrMatrix.Encode(messenger.Identity.CodeText);
    }

    public override string Title => UiText.InPersonTitle;

    public string Name => messenger.Identity.Name.Value;

    public string Fingerprint => messenger.Identity.Fingerprint.ToGroupedString();

    public QrMatrix QrCode { get; }

    public bool HasStatus => StatusMessage is not null;

    public bool HasPreview => PreviewName is not null;

    public bool HasScannedContact => ScannedFingerprint is not null;

    public static QrScanRequest ScanRequest { get; } = new(UiText.ScanTitle, UiText.ScanHint, UiText.ScanCancel, UiText.ScanStarting);

    private bool CanScan => !IsBusy;

    private bool CanAdd => !IsBusy && pending is { CanAdd: true } && AddText is not null;

    public override void OnShown()
    {
        messenger.Contacts.Changed += OnContactChanged;
        RefreshConnection();
    }

    public override void OnHidden() => messenger.Contacts.Changed -= OnContactChanged;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        ClearPreview();
        try
        {
            QrScanResult result = await scanner.ScanAsync(ScanRequest, cancellationToken);
            switch (result.Status)
            {
                case QrScanStatus.Done when result.IsRead:
                    OffersTextCodes = false;
                    await TakeAsync(messenger.Contacts.Check(result.Text), cancellationToken);
                    break;
                case QrScanStatus.Cancelled:
                    break;
                case QrScanStatus.NoCamera:
                    Offer(UiText.InPersonNoCamera);
                    break;
                case QrScanStatus.CameraRefused:
                    Offer(UiText.InPersonCameraRefused);
                    break;
                default:
                    Offer(UiText.InPersonCameraFailed);
                    break;
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            Offer(UiText.InPersonCameraFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        if (pending is not { CanAdd: true } check)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveAsync(check, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void OpenChat()
    {
        if (scannedKey is { } key && messenger.Contacts.TryGet(key, out Contact? contact))
        {
            navigation.Replace(pages.Conversation(contact));
        }
    }

    [RelayCommand]
    private void AddByCode() => navigation.Replace(pages.AddContact());

    private async Task TakeAsync(CodeCheck check, CancellationToken cancellationToken)
    {
        switch (check.Outcome)
        {
            case CodeCheckOutcome.New:
                await SaveAsync(check, cancellationToken);
                return;
            case CodeCheckOutcome.AlreadyContact when check.Code is not null && check.Existing is { } existing:
                if (await messenger.Contacts.MarkScannedInPersonAsync(check, cancellationToken))
                {
                    Track(existing, Formats.With(Formats.InPersonAlreadyChecked, existing.Name.Value));
                }
                else
                {
                    ErrorMessage = UiText.AddContactStale;
                }

                return;
            case CodeCheckOutcome.NewConnectionKeys when check.Code is not null:
                Ask(check, Formats.With(Formats.NewKeys, check.Existing?.Name.Value), UiText.AddContactAcceptNewKeys);
                return;
            case CodeCheckOutcome.LooksLikeContact when check.Code is not null:
                Ask(check, Formats.With(Formats.LooksLike, check.Existing?.Name.Value), UiText.AddContactAddAsNew);
                return;
        }

        ErrorMessage = check.Outcome switch
        {
            CodeCheckOutcome.UnsupportedVersion => UiText.AddContactUnsupportedVersion,
            CodeCheckOutcome.Damaged => UiText.AddContactDamaged,
            CodeCheckOutcome.OwnCode => UiText.AddContactOwnCode,
            CodeCheckOutcome.KeysInUse => UiText.AddContactKeysInUse,
            _ => UiText.InPersonNotACode,
        };
    }

    private async Task SaveAsync(CodeCheck check, CancellationToken cancellationToken)
    {
        AddContactResult result = await messenger.Contacts.AddAsync(check, CodeExchange.InPerson, cancellationToken);
        ClearPreview();
        if (!result.IsSaved)
        {
            ErrorMessage = UiText.AddContactStale;
            return;
        }

        CompositeFormat format = result.Outcome == AddContactOutcome.KeysReplaced ? Formats.InPersonKeysAccepted : Formats.InPersonAdded;
        Track(result.Contact, Formats.With(format, result.Contact.Name.Value));
    }

    private void Ask(CodeCheck check, string hint, string add)
    {
        pending = check;
        PreviewName = check.Code?.DisplayName.Value;
        PreviewFingerprint = check.Code?.Fingerprint.ToGroupedString();
        PreviewHint = hint;
        AddText = add;
    }

    private void Track(Contact contact, string status)
    {
        scannedKey = contact.IdentityKey;
        ScannedFingerprint = contact.Fingerprint.ToGroupedString();
        StatusMessage = status;
        IsConnected = contact.IsAdded;
        RefreshConnection();
    }

    private void Offer(string message)
    {
        ErrorMessage = message;
        OffersTextCodes = true;
    }

    private void ClearPreview()
    {
        pending = null;
        PreviewName = null;
        PreviewFingerprint = null;
        PreviewHint = null;
        AddText = null;
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e) => uiThread.Post(RefreshConnection);

    private void RefreshConnection()
    {
        if (IsConnected || scannedKey is not { } key || !messenger.Contacts.TryGet(key, out Contact? contact) || !contact.IsAdded)
        {
            return;
        }

        IsConnected = true;
        StatusMessage = Formats.With(Formats.InPersonConnected, contact.Name.Value);
    }
}
