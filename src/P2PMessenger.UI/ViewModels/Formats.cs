using System.Globalization;
using System.Text;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

internal static class Formats
{
    public static readonly CompositeFormat Unread = CompositeFormat.Parse(UiText.ContactUnreadFormat);
    public static readonly CompositeFormat ContactAdded = CompositeFormat.Parse(UiText.ContactAddedNoticeFormat);
    public static readonly CompositeFormat FileName = CompositeFormat.Parse(UiText.ProfileFileNameFormat);
    public static readonly CompositeFormat ImageFileName = CompositeFormat.Parse(UiText.ProfileImageFileNameFormat);
    public static readonly CompositeFormat AlreadyContact = CompositeFormat.Parse(UiText.AddContactAlreadyContactFormat);
    public static readonly CompositeFormat NewKeys = CompositeFormat.Parse(UiText.AddContactNewKeysFormat);
    public static readonly CompositeFormat LooksLike = CompositeFormat.Parse(UiText.AddContactLooksLikeFormat);
    public static readonly CompositeFormat Waiting = CompositeFormat.Parse(UiText.ConversationWaitingFormat);
    public static readonly CompositeFormat KeyNotCompared = CompositeFormat.Parse(UiText.ConversationKeyNotComparedFormat);
    public static readonly CompositeFormat KeyNotComparedInPerson = CompositeFormat.Parse(UiText.ConversationKeyNotComparedInPersonFormat);
    public static readonly CompositeFormat InPersonAdded = CompositeFormat.Parse(UiText.InPersonAddedFormat);
    public static readonly CompositeFormat InPersonKeysAccepted = CompositeFormat.Parse(UiText.InPersonKeysAcceptedFormat);
    public static readonly CompositeFormat InPersonAlreadyChecked = CompositeFormat.Parse(UiText.InPersonAlreadyCheckedFormat);
    public static readonly CompositeFormat InPersonConnected = CompositeFormat.Parse(UiText.InPersonConnectedFormat);
    public static readonly CompositeFormat NotAdded = CompositeFormat.Parse(UiText.ConversationNotAddedFormat);
    public static readonly CompositeFormat VerifyIntro = CompositeFormat.Parse(UiText.VerifyIntroFormat);
    public static readonly CompositeFormat Theirs = CompositeFormat.Parse(UiText.VerifyTheirsFormat);
    public static readonly CompositeFormat Checked = CompositeFormat.Parse(UiText.VerifyCheckedFormat);
    public static readonly CompositeFormat NotCompared = CompositeFormat.Parse(UiText.VerifyNotComparedFormat);
    public static readonly CompositeFormat Listening = CompositeFormat.Parse(UiText.NetworkListeningFormat);
    public static readonly CompositeFormat Tor = CompositeFormat.Parse(UiText.NetworkTorFormat);
    public static readonly CompositeFormat TorStarting = CompositeFormat.Parse(UiText.ContactListTorStartingFormat);
    public static readonly CompositeFormat Discovery = CompositeFormat.Parse(UiText.NetworkDiscoveryFormat);
    public static readonly CompositeFormat DiscoveryUnavailable = CompositeFormat.Parse(UiText.NetworkDiscoveryUnavailableFormat);
    public static readonly CompositeFormat Heard = CompositeFormat.Parse(UiText.NetworkHeardFormat);
    public static readonly CompositeFormat PassphraseHint = CompositeFormat.Parse(UiText.PassphraseHintFormat);
    public static readonly CompositeFormat PassphraseTooShort = CompositeFormat.Parse(UiText.PassphraseTooShortFormat);
    public static readonly CompositeFormat PassphraseStateSet = CompositeFormat.Parse(UiText.PassphraseStateSetFormat);
    public static readonly CompositeFormat DeleteChatQuestion = CompositeFormat.Parse(UiText.ConversationDeleteChatQuestionFormat);
    public static readonly CompositeFormat ChatDeleted = CompositeFormat.Parse(UiText.ConversationChatDeletedFormat);
    public static readonly CompositeFormat MessageDeleted = CompositeFormat.Parse(UiText.ConversationMessageDeletedFormat);
    public static readonly CompositeFormat GroupItemMembers = CompositeFormat.Parse(UiText.GroupItemMembersFormat);
    public static readonly CompositeFormat GroupItemWaiting = CompositeFormat.Parse(UiText.GroupItemWaitingFormat);
    public static readonly CompositeFormat NewGroupTooMany = CompositeFormat.Parse(UiText.NewGroupTooManyFormat);
    public static readonly CompositeFormat GroupMembers = CompositeFormat.Parse(UiText.GroupMembersFormat);
    public static readonly CompositeFormat GroupYou = CompositeFormat.Parse(UiText.GroupYouFormat);
    public static readonly CompositeFormat GroupWaiting = CompositeFormat.Parse(UiText.GroupWaitingFormat);
    public static readonly CompositeFormat GroupListsDiffer = CompositeFormat.Parse(UiText.GroupListsDifferFormat);
    public static readonly CompositeFormat GroupCreatedBy = CompositeFormat.Parse(UiText.GroupCreatedByFormat);
    public static readonly CompositeFormat GroupDeliveryReceived = CompositeFormat.Parse(UiText.GroupDeliveryReceivedFormat);
    public static readonly CompositeFormat GroupDeliveryMember = CompositeFormat.Parse(UiText.GroupDeliveryMemberFormat);
    public static readonly CompositeFormat GroupDeleteChatQuestion = CompositeFormat.Parse(UiText.GroupDeleteChatQuestionFormat);
    public static readonly CompositeFormat GroupDeleteInactiveChatQuestion = CompositeFormat.Parse(UiText.GroupDeleteInactiveChatQuestionFormat);
    public static readonly CompositeFormat GroupMembersButton = CompositeFormat.Parse(UiText.GroupMembersButtonFormat);
    public static readonly CompositeFormat GroupRemoveMemberQuestion = CompositeFormat.Parse(UiText.GroupRemoveMemberQuestionFormat);
    public static readonly CompositeFormat GroupRemoveLastMemberQuestion = CompositeFormat.Parse(UiText.GroupRemoveLastMemberQuestionFormat);
    public static readonly CompositeFormat GroupMemberRemoved = CompositeFormat.Parse(UiText.GroupMemberRemovedFormat);
    public static readonly CompositeFormat GroupMemberRemovedEnded = CompositeFormat.Parse(UiText.GroupMemberRemovedEndedFormat);
    public static readonly CompositeFormat GroupLeaveQuestion = CompositeFormat.Parse(UiText.GroupLeaveQuestionFormat);
    public static readonly CompositeFormat GroupLeaveAdminQuestion = CompositeFormat.Parse(UiText.GroupLeaveAdminQuestionFormat);
    public static readonly CompositeFormat GroupLeaveLastMemberQuestion = CompositeFormat.Parse(UiText.GroupLeaveLastMemberQuestionFormat);
    public static readonly CompositeFormat CopiedChatItemSummary = CompositeFormat.Parse(UiText.CopiedChatItemSummaryFormat);
    public static readonly CompositeFormat CopiedChatItemSummaryOne = CompositeFormat.Parse(UiText.CopiedChatItemSummaryOneFormat);
    public static readonly CompositeFormat CopiedConversationBanner = CompositeFormat.Parse(UiText.CopiedConversationBannerFormat);
    public static readonly CompositeFormat CopiedConversationDeleteQuestion = CompositeFormat.Parse(UiText.CopiedConversationDeleteQuestionFormat);
    public static readonly CompositeFormat ReceiveCopyReceiving = CompositeFormat.Parse(UiText.ReceiveCopyReceivingFormat);
    public static readonly CompositeFormat ReceiveCopyCompleted = CompositeFormat.Parse(UiText.ReceiveCopyCompletedFormat);
    public static readonly CompositeFormat SendCopyCandidateContact = CompositeFormat.Parse(UiText.SendCopyCandidateContactFormat);
    public static readonly CompositeFormat SendCopyCandidateContactTruncated = CompositeFormat.Parse(UiText.SendCopyCandidateContactTruncatedFormat);
    public static readonly CompositeFormat SendCopyCandidateGroup = CompositeFormat.Parse(UiText.SendCopyCandidateGroupFormat);
    public static readonly CompositeFormat SendCopyCandidateGroupTruncated = CompositeFormat.Parse(UiText.SendCopyCandidateGroupTruncatedFormat);
    public static readonly CompositeFormat SendCopySending = CompositeFormat.Parse(UiText.SendCopySendingFormat);
    public static readonly CompositeFormat SendCopyCompleted = CompositeFormat.Parse(UiText.SendCopyCompletedFormat);

    public static string With(CompositeFormat format, object? argument) => string.Format(CultureInfo.CurrentCulture, format, argument);

    public static string WithAll(CompositeFormat format, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}
