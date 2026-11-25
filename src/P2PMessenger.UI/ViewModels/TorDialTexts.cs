using P2PMessenger.Application.Connections;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class TorDialTexts
{
    public static string Attempt(bool usesTor, Reachability reachability, TorDialStatus status)
    {
        if (reachability == Reachability.Tor)
        {
            return UiText.NetworkTorConnected;
        }

        if (!usesTor)
        {
            return UiText.NetworkTorNotUsed;
        }

        return status switch
        {
            TorDialStatus.Connecting => UiText.NetworkTorConnecting,
            TorDialStatus.NotFound => UiText.NetworkTorNotFound,
            TorDialStatus.NoAnswer => UiText.NetworkTorNoAnswer,
            TorDialStatus.TimedOut => UiText.NetworkTorTimedOut,
            TorDialStatus.Refused => UiText.NetworkTorRefused,
            TorDialStatus.Failed => UiText.NetworkTorFailed,
            _ => UiText.NetworkTorIdle,
        };
    }
}
