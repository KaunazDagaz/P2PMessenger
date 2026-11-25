using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Core;
using P2PMessenger.UI.Qr;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class ProfileViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly IClipboardService clipboard;
    private readonly ICodeFileService codeFiles;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(SaveCommand), nameof(SaveImageCommand))]
    private bool isBusy;

    public ProfileViewModel(Messenger messenger, IClipboardService clipboard, ICodeFileService codeFiles)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(codeFiles);

        this.messenger = messenger;
        this.clipboard = clipboard;
        this.codeFiles = codeFiles;
        QrCode = QrMatrix.Encode(CodeText);
    }

    public override string Title => UiText.ProfileTitle;

    public string Name => messenger.Identity.Name.Value;

    public string Fingerprint => messenger.Identity.Fingerprint.ToGroupedString();

    public string CodeText => messenger.Identity.CodeText;

    public QrMatrix QrCode { get; }

    private bool IsIdle => !IsBusy;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CopyAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            if (await clipboard.SetTextAsync(CodeText))
            {
                StatusMessage = UiText.ProfileCopied;
            }
            else
            {
                ErrorMessage = UiText.ProfileCopyFailed;
            }
        }
        catch (Exception exception) when (Failures.IsRecoverable(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.ProfileCopyFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SaveAsync(CancellationToken cancellationToken) =>
        SavingAsync(() => codeFiles.SaveAsync(Formats.With(Formats.FileName, Name), CodeText, cancellationToken), UiText.ProfileSaved);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SaveImageAsync(CancellationToken cancellationToken) =>
        SavingAsync(() => codeFiles.SaveImageAsync(Formats.With(Formats.ImageFileName, Name), QrPng.Render(QrCode), cancellationToken), UiText.ProfileImageSaved);

    private async Task SavingAsync(Func<Task<CodeFileResult>> save, string saved)
    {
        IsBusy = true;
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            CodeFileResult result = await save();
            switch (result.Status)
            {
                case CodeFileStatus.Done:
                    StatusMessage = saved;
                    break;
                case CodeFileStatus.Cancelled:
                    break;
                default:
                    ErrorMessage = UiText.ProfileSaveFailed;
                    break;
            }
        }
        catch (Exception exception) when (Failures.IsFailure(exception))
        {
            ReportFailure(exception);
            ErrorMessage = UiText.ProfileSaveFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
