using P2PMessenger.Application.Internet;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public static class InternetRouteLines
{
    public static string Describe(InternetRouteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.State switch
        {
            InternetRouteState.Connected => UiText.ContactListTorConnected,
            InternetRouteState.Off => UiText.ContactListTorOff,
            InternetRouteState.Starting => Formats.With(Formats.TorStarting, status.Progress),
            _ => status.Problem switch
            {
                InternetRouteProblem.NoNetwork => UiText.ContactListTorNoNetwork,
                InternetRouteProblem.ClockWrong => UiText.ContactListTorClockWrong,
                InternetRouteProblem.CannotReachTor => UiText.ContactListTorCannotReach,
                InternetRouteProblem.TorFailed => UiText.ContactListTorFailed,
                InternetRouteProblem.TorStopped => UiText.ContactListTorStopped,
                _ => UiText.ContactListTorNotInBuild,
            },
        };
    }
}
