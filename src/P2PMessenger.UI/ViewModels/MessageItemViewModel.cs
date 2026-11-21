using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateExplanation))]
    private DeliveryState delivery;

    [ObservableProperty]
    private bool isExplanationShown;

    public MessageItemViewModel(ChatMessage message, string sender, TimeZoneInfo timeZone)
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

    public string? StateExplanation => Delivery switch
    {
        DeliveryState.Waiting => UiText.DeliveryWaitingExplanation,
        DeliveryState.Received => UiText.DeliveryReceivedExplanation,
        _ => null,
    };

    internal void Update(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Delivery = message.Delivery;
    }

    [RelayCommand]
    private void ToggleExplanation() => IsExplanationShown = !IsExplanationShown;
}
