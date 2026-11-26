using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupMessageItemViewModel : ViewModelBase
{
    private readonly Func<GroupMessageItemViewModel, CancellationToken, Task>? delete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StateExplanation), nameof(CanDelete), nameof(IsDeleteVisible))]
    private int receivedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteVisible))]
    private bool isExplanationShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteVisible))]
    private bool isConfirmingDelete;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand), nameof(CancelDeleteCommand))]
    private bool isDeleting;

    public GroupMessageItemViewModel(GroupChatMessage message, string sender, TimeZoneInfo timeZone)
        : this(message, sender, timeZone, null)
    {
    }

    public GroupMessageItemViewModel(GroupChatMessage message, string sender, TimeZoneInfo timeZone, Func<GroupMessageItemViewModel, CancellationToken, Task>? delete)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(timeZone);

        Id = message.Id;
        IsOwn = message.Direction == MessageDirection.Outgoing;
        Sender = sender;
        WrittenAt = TimeZoneInfo.ConvertTime(message.WrittenAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        Text = message.Text;
        RecipientCount = message.RecipientCount;
        ReceivedCount = message.ReceivedCount;
        this.delete = delete;
    }

    public MessageId Id { get; }

    public bool IsOwn { get; }

    public bool IsTheirs => !IsOwn;

    public string Sender { get; }

    public string WrittenAt { get; }

    public string Text { get; }

    public int RecipientCount { get; }

    public string? StateText => !IsOwn ? null
        : ReceivedCount == 0 ? UiText.DeliveryWaiting
        : Formats.WithAll(Formats.GroupDeliveryReceived, ReceivedCount, RecipientCount);

    public string? StateExplanation => !IsOwn ? null
        : ReceivedCount == 0 ? UiText.GroupDeliveryWaitingExplanation
        : UiText.GroupDeliveryReceivedExplanation;

    public bool CanDelete => delete is not null && IsOwn && RecipientCount > 0 && ReceivedCount == 0;

    public bool IsDeleteVisible => CanDelete && IsExplanationShown && !IsConfirmingDelete;

    private bool IsIdle => !IsDeleting;

    internal void Update(GroupChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ReceivedCount = message.ReceivedCount;
        if (!CanDelete)
        {
            IsConfirmingDelete = false;
        }
    }

    [RelayCommand]
    private void ToggleExplanation() => IsExplanationShown = !IsExplanationShown;

    [RelayCommand]
    private void AskDelete()
    {
        if (CanDelete)
        {
            IsConfirmingDelete = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmDeleteAsync(CancellationToken cancellationToken)
    {
        if (delete is null || !CanDelete)
        {
            IsConfirmingDelete = false;
            return;
        }

        IsDeleting = true;
        try
        {
            await delete(this, cancellationToken);
        }
        finally
        {
            IsDeleting = false;
            IsConfirmingDelete = false;
        }
    }
}
