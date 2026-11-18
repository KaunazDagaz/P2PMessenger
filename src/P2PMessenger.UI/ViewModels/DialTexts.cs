using P2PMessenger.Application.Connections;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class DialTexts
{
    public static string? Failure(DialStatus status) => status switch
    {
        DialStatus.Unreachable => UiText.DialUnreachable,
        DialStatus.TimedOut => UiText.DialTimedOut,
        DialStatus.Refused => UiText.DialRefused,
        _ => null,
    };
}
