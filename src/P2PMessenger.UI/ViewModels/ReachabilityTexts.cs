using P2PMessenger.Application.Connections;
using P2PMessenger.Application.Contacts;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class ReachabilityTexts
{
    public static string Status(Contact contact, Reachability reachability)
    {
        ArgumentNullException.ThrowIfNull(contact);

        if (!contact.IsAdded)
        {
            return UiText.ContactWaiting;
        }

        return reachability == Reachability.Nearby ? UiText.ContactReachableNearby : UiText.ContactNotReachable;
    }
}
