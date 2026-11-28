using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    private readonly Func<MessageItemViewModel, CancellationToken, Task>? delete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanDelete), nameof(IsDeleteVisible))]
    private DeliveryState delivery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteVisible))]
    private bool isConfirmingDelete;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand), nameof(CancelDeleteCommand))]
    private bool isDeleting;

    public MessageItemViewModel(ChatMessage message, string sender, TimeZoneInfo timeZone)
        : this(message, sender, timeZone, null)
    {
    }

    public MessageItemViewModel(ChatMessage message, string sender, TimeZoneInfo timeZone, Func<MessageItemViewModel, CancellationToken, Task>? delete)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(timeZone);

        Id = message.Id;
        IsOwn = message.Direction == MessageDirection.Outgoing;
        Sender = sender;
        WrittenAt = TimeZoneInfo.ConvertTime(message.WrittenAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        Text = message.Text;
        Delivery = message.Delivery;
        this.delete = delete;
    }

    public MessageId Id { get; }

    public bool IsOwn { get; }

    public bool IsTheirs => !IsOwn;

    public string Sender { get; }

    public string WrittenAt { get; }

    public string Text { get; }

    public string? StateText => Delivery switch
    {
        DeliveryState.Waiting => UiText.DeliveryWaiting,
        DeliveryState.Received => UiText.DeliveryReceived,
        _ => null,
    };

    public bool CanDelete => delete is not null && IsOwn && Delivery == DeliveryState.Waiting;

    public bool IsDeleteVisible => CanDelete && !IsConfirmingDelete;

    private bool IsIdle => !IsDeleting;

    internal void Update(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Delivery = message.Delivery;
        if (!CanDelete)
        {
            IsConfirmingDelete = false;
        }
    }

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
