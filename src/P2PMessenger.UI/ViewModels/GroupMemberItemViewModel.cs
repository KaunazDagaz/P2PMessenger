using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupMemberItemViewModel : ViewModelBase
{
    private readonly Func<GroupMemberItemViewModel, CancellationToken, Task> remove;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoveButtonVisible))]
    private bool isConfirmingRemove;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveCommand), nameof(CancelRemoveCommand))]
    private bool isRemoving;

    public GroupMemberItemViewModel(
        Ed25519PublicKey identityKey,
        string name,
        bool isYou,
        bool isAdmin,
        bool canRemove,
        string removeQuestion,
        Func<GroupMemberItemViewModel, CancellationToken, Task> remove)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(removeQuestion);
        ArgumentNullException.ThrowIfNull(remove);

        IdentityKey = identityKey;
        Name = name;
        DisplayName = isYou ? Formats.With(Formats.GroupYou, name) : name;
        Fingerprint = Core.Identity.Fingerprint.Of(identityKey).ToGroupedString();
        IsYou = isYou;
        IsAdmin = isAdmin;
        CanRemove = canRemove;
        RemoveQuestion = removeQuestion;
        this.remove = remove;
    }

    public Ed25519PublicKey IdentityKey { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string Fingerprint { get; }

    public bool IsYou { get; }

    public bool IsAdmin { get; }

    public bool CanRemove { get; }

    public string RemoveQuestion { get; }

    public bool IsRemoveButtonVisible => CanRemove && !IsConfirmingRemove;

    private bool IsIdle => !IsRemoving;

    [RelayCommand]
    private void AskRemove()
    {
        if (CanRemove)
        {
            IsConfirmingRemove = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmRemoveAsync(CancellationToken cancellationToken)
    {
        if (!CanRemove)
        {
            return;
        }

        IsRemoving = true;
        try
        {
            await remove(this, cancellationToken);
        }
        finally
        {
            IsRemoving = false;
            IsConfirmingRemove = false;
        }
    }
}
