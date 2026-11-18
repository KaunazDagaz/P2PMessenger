using System.Globalization;
using P2PMessenger.Application.Chats;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.UI.ViewModels;

public sealed class MessageItemViewModel : ViewModelBase
{
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
    }

    public MessageId Id { get; }

    public bool IsOwn { get; }

    public bool IsTheirs => !IsOwn;

    public string Sender { get; }

    public string WrittenAt { get; }

    public string Text { get; }
}
