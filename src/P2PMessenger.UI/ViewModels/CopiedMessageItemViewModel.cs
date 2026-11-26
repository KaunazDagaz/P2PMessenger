using System.Globalization;
using P2PMessenger.Application.Copy;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed class CopiedMessageItemViewModel : ViewModelBase
{
    public CopiedMessageItemViewModel(CopiedMessage message, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(timeZone);

        IsOwn = message.WrittenByOwner;
        Sender = IsOwn ? UiText.CopiedConversationYou : message.AuthorName!.Value;
        WrittenAt = TimeZoneInfo.ConvertTime(message.WrittenAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        Text = message.Text;
    }

    public bool IsOwn { get; }

    public bool IsTheirs => !IsOwn;

    public string Sender { get; }

    public string WrittenAt { get; }

    public string Text { get; }
}
