using System.Globalization;
using System.Text;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class Formats
{
    public static readonly CompositeFormat Unread = CompositeFormat.Parse(UiText.ContactUnreadFormat);
    public static readonly CompositeFormat ContactAdded = CompositeFormat.Parse(UiText.ContactAddedNoticeFormat);
    public static readonly CompositeFormat FileName = CompositeFormat.Parse(UiText.ProfileFileNameFormat);
    public static readonly CompositeFormat AlreadyContact = CompositeFormat.Parse(UiText.AddContactAlreadyContactFormat);
    public static readonly CompositeFormat NewKeys = CompositeFormat.Parse(UiText.AddContactNewKeysFormat);
    public static readonly CompositeFormat LooksLike = CompositeFormat.Parse(UiText.AddContactLooksLikeFormat);
    public static readonly CompositeFormat Waiting = CompositeFormat.Parse(UiText.ConversationWaitingFormat);
    public static readonly CompositeFormat KeyNotCompared = CompositeFormat.Parse(UiText.ConversationKeyNotComparedFormat);
    public static readonly CompositeFormat NotConnected = CompositeFormat.Parse(UiText.ConversationNotConnectedFormat);
    public static readonly CompositeFormat NotAdded = CompositeFormat.Parse(UiText.ConversationNotAddedFormat);
    public static readonly CompositeFormat VerifyIntro = CompositeFormat.Parse(UiText.VerifyIntroFormat);
    public static readonly CompositeFormat Theirs = CompositeFormat.Parse(UiText.VerifyTheirsFormat);
    public static readonly CompositeFormat Checked = CompositeFormat.Parse(UiText.VerifyCheckedFormat);
    public static readonly CompositeFormat NotCompared = CompositeFormat.Parse(UiText.VerifyNotComparedFormat);
    public static readonly CompositeFormat Listening = CompositeFormat.Parse(UiText.NetworkListeningFormat);
    public static readonly CompositeFormat PortInUse = CompositeFormat.Parse(UiText.NetworkPortInUseFormat);

    public static string With(CompositeFormat format, object? argument) => string.Format(CultureInfo.CurrentCulture, format, argument);
}
