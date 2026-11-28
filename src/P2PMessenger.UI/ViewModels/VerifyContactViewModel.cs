using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.UI.Resources;
using P2PMessenger.UI.Services;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class VerifyContactViewModel : PageViewModel
{
    private readonly Messenger messenger;
    private readonly Ed25519PublicKey contactKey;
    private readonly IUiThread uiThread;

    [ObservableProperty]
    private string firstLabel = string.Empty;

    [ObservableProperty]
    private string firstFingerprint = string.Empty;

    [ObservableProperty]
    private string secondLabel = string.Empty;

    [ObservableProperty]
    private string secondFingerprint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotChecked))]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand), nameof(NoMatchCommand), nameof(UncheckCommand))]
    private bool isChecked;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string? mismatchText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand), nameof(NoMatchCommand), nameof(UncheckCommand))]
    private bool isBusy;

    public VerifyContactViewModel(Messenger messenger, Ed25519PublicKey contactKey, IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(contactKey);
        ArgumentNullException.ThrowIfNull(uiThread);

        this.messenger = messenger;
        this.contactKey = contactKey;
        this.uiThread = uiThread;
        Refresh();
    }

    public override string Title => UiText.VerifyTitle;

    public bool IsNotChecked => !IsChecked;

    private bool CanMark => !IsBusy && !IsChecked;

    private bool CanUncheck => !IsBusy && IsChecked;

    public override void OnShown()
    {
        messenger.Contacts.Changed += OnContactChanged;
        Refresh();
    }

    public override void OnHidden() => messenger.Contacts.Changed -= OnContactChanged;

    [RelayCommand(CanExecute = nameof(CanMark))]
    private Task MatchAsync(CancellationToken cancellationToken)
    {
        MismatchText = null;
        return SetAsync(KeyComparison.CheckedByYou, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanMark))]
    private void NoMatch() => MismatchText = UiText.VerifyMismatch;

    [RelayCommand(CanExecute = nameof(CanUncheck))]
    private Task UncheckAsync(CancellationToken cancellationToken) => SetAsync(KeyComparison.NotCompared, cancellationToken);

    private async Task SetAsync(KeyComparison keyComparison, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await messenger.Contacts.SetKeyComparisonAsync(contactKey, keyComparison, cancellationToken);
            Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnContactChanged(object? sender, ContactChangedEventArgs e)
    {
        if (e.Contact.IdentityKey.Equals(contactKey))
        {
            uiThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        if (!messenger.Contacts.TryGet(contactKey, out Contact? contact))
        {
            return;
        }

        string name = contact.Name.Value;
        FingerprintPair pair = FingerprintPair.Create(messenger.Identity.Keys.IdentityKey, contact.IdentityKey);
        string yours = UiText.VerifyYoursLabel;
        string theirs = Formats.With(Formats.Theirs, name);
        FirstLabel = pair.OwnFirst ? yours : theirs;
        FirstFingerprint = pair.First.ToGroupedString();
        SecondLabel = pair.OwnFirst ? theirs : yours;
        SecondFingerprint = pair.Second.ToGroupedString();
        IsChecked = contact.KeyComparison == KeyComparison.CheckedByYou;
        StatusText = Formats.With(IsChecked ? Formats.Checked : Formats.NotCompared, name);
    }
}
