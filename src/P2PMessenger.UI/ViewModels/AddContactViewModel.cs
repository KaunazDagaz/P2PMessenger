using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class AddContactViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly ICodeFileService codeFiles;
    private readonly INavigationService navigation;
    private readonly Pages pages;
    private CodeCheck? check;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private string codeText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(OpenFileCommand), nameof(AddCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? previewName;

    [ObservableProperty]
    private string? previewFingerprint;

    [ObservableProperty]
    private string? previewHint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyPropertyChangedFor(nameof(CanAddPreview))]
    private string? addText;

    public AddContactViewModel(Messenger messenger, ICodeFileService codeFiles, INavigationService navigation, Pages pages)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(codeFiles);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(pages);

        this.messenger = messenger;
        this.codeFiles = codeFiles;
        this.navigation = navigation;
        this.pages = pages;
    }

    public override string Title => UiText.AddContactTitle;

    public bool HasPreview => PreviewName is not null;

    public bool CanAddPreview => AddText is not null;

    private bool CanCheck => !IsBusy && !string.IsNullOrWhiteSpace(CodeText);

    private bool CanOpen => !IsBusy;

    private bool CanAdd => !IsBusy && check is { CanAdd: true } && AddText is not null;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void Check() => Show(messenger.Contacts.Check(CodeText));

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            CodeFileResult result = await codeFiles.OpenAsync(cancellationToken);
            switch (result.Status)
            {
                case CodeFileStatus.Done:
                    CodeText = result.Text ?? string.Empty;
                    Show(messenger.Contacts.Check(CodeText));
                    break;
                case CodeFileStatus.TooLarge:
                    ErrorMessage = UiText.AddContactFileTooLarge;
                    break;
                case CodeFileStatus.NoCodeInImage:
                    ErrorMessage = UiText.AddContactNoCodeInImage;
                    break;
                case CodeFileStatus.Failed:
                    ErrorMessage = UiText.AddContactFileUnreadable;
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            ErrorMessage = UiText.AddContactFileUnreadable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        if (check is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            AddContactResult result = await messenger.Contacts.AddAsync(check, cancellationToken);
            if (!result.IsSaved)
            {
                ErrorMessage = UiText.AddContactStale;
                ClearPreview();
                return;
            }

            navigation.Replace(pages.Conversation(result.Contact));
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCodeTextChanged(string value)
    {
        ClearPreview();
        ErrorMessage = null;
    }

    private void Show(CodeCheck result)
    {
        ClearPreview();
        ErrorMessage = result.Outcome switch
        {
            CodeCheckOutcome.NotACode => UiText.AddContactNotACode,
            CodeCheckOutcome.UnsupportedVersion => UiText.AddContactUnsupportedVersion,
            CodeCheckOutcome.Damaged => UiText.AddContactDamaged,
            CodeCheckOutcome.OwnCode => UiText.AddContactOwnCode,
            CodeCheckOutcome.KeysInUse => UiText.AddContactKeysInUse,
            CodeCheckOutcome.AlreadyContact => Formats.With(Formats.AlreadyContact, result.Existing?.Name.Value),
            _ => null,
        };

        if (!result.CanAdd)
        {
            return;
        }

        check = result;
        PreviewName = result.Code.DisplayName.Value;
        PreviewFingerprint = result.Code.Fingerprint.ToGroupedString();
        (PreviewHint, AddText) = result.Outcome switch
        {
            CodeCheckOutcome.NewConnectionKeys => (Formats.With(Formats.NewKeys, result.Existing?.Name.Value), UiText.AddContactAcceptNewKeys),
            CodeCheckOutcome.LooksLikeContact => (Formats.With(Formats.LooksLike, result.Existing?.Name.Value), UiText.AddContactAddAsNew),
            _ => (UiText.AddContactNewHint, UiText.AddContactAdd),
        };
    }

    private void ClearPreview()
    {
        check = null;
        PreviewName = null;
        PreviewFingerprint = null;
        PreviewHint = null;
        AddText = null;
    }
}
