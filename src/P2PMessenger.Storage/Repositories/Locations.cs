using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Repositories;

internal static class Locations
{
    public const long IdentityRow = 1;

    public static RecordLocation IdentityKeySet { get; } = new("identity", "key_set_enc", IdentityRow);

    public static RecordLocation PersonIdentityKey(long personId) => new("person", "identity_key_enc", personId);

    public static RecordLocation PersonName(long personId) => new("person", "display_name_enc", personId);

    public static RecordLocation ContactStaticKey(long personId) => new("contact", "static_key_enc", personId);

    public static RecordLocation ContactOnionKey(long personId) => new("contact", "onion_key_enc", personId);

    public static RecordLocation ContactSignature(long personId) => new("contact", "code_signature_enc", personId);

    public static RecordLocation MessageId(long messageId) => new("message", "message_id_enc", messageId);

    public static RecordLocation MessageText(long messageId) => new("message", "text_enc", messageId);

    public static RecordLocation MessageWrittenAt(long messageId) => new("message", "written_at_enc", messageId);

    public static RecordLocation MessageRecordedAt(long messageId) => new("message", "recorded_at_enc", messageId);

    public static RecordLocation ConversationSentSequence(long conversationId) => new("conversation", "sent_sequence_enc", conversationId);

    public static RecordLocation ConversationDeletedRanges(long conversationId) => new("conversation", "deleted_ranges_enc", conversationId);

    public static RecordLocation MessageGroupEnvelope(long messageId) => new("message", "group_envelope_enc", messageId);

    public static RecordLocation GroupSharedId(long groupId) => new("chat_group", "shared_id_enc", groupId);

    public static RecordLocation GroupName(long groupId) => new("chat_group", "name_enc", groupId);

    public static RecordLocation GroupAuthority(long groupId) => new("chat_group", "authority_enc", groupId);

    public static RecordLocation GroupKey(long groupId, long epoch) => new("group_key", "key_enc", GroupKeyRow(groupId, epoch));

    public static RecordLocation GroupRecord(long groupId, long epoch) => new("group_key", "record_enc", GroupKeyRow(groupId, epoch));

    public static RecordLocation GroupLeaveProof(long groupId, long memberPersonId) =>
        new("group_leave_proof", "proof_enc", GroupLeaveProofRow(groupId, memberPersonId));

    public static RecordLocation GroupAcknowledgement(long messageId, long recipientPersonId) =>
        new("group_acknowledgement", "ack_enc", GroupAcknowledgementRow(messageId, recipientPersonId));

    public static RecordLocation CopiedConversationOrigin(long conversationId) => new("copied_conversation", "origin_enc", conversationId);

    public static RecordLocation CopiedConversationTitle(long conversationId) => new("copied_conversation", "title_enc", conversationId);

    public static RecordLocation CopiedConversationCopiedAt(long conversationId) => new("copied_conversation", "copied_at_enc", conversationId);

    public static RecordLocation CopiedMessageAuthorName(long conversationId, long position, bool writtenByOwner) =>
        new("copied_message", "author_name_enc", CopiedMessageRow(conversationId, position, writtenByOwner));

    public static RecordLocation CopiedMessageText(long conversationId, long position, bool writtenByOwner) =>
        new("copied_message", "text_enc", CopiedMessageRow(conversationId, position, writtenByOwner));

    public static RecordLocation CopiedMessageWrittenAt(long conversationId, long position, bool writtenByOwner) =>
        new("copied_message", "written_at_enc", CopiedMessageRow(conversationId, position, writtenByOwner));

    private static long GroupKeyRow(long groupId, long epoch) =>
        groupId is >= 1 and <= int.MaxValue && epoch is >= 1 and <= uint.MaxValue
            ? (groupId << 32) | epoch
            : throw new ArgumentOutOfRangeException(nameof(groupId), "A group key row needs a group row from 1 to 2^31 - 1 and an epoch from 1 to 2^32 - 1.");

    private static long GroupAcknowledgementRow(long messageId, long recipientPersonId) =>
        messageId is >= 1 and <= int.MaxValue && recipientPersonId is >= 1 and <= uint.MaxValue
            ? (messageId << 32) | recipientPersonId
            : throw new ArgumentOutOfRangeException(nameof(messageId), "A group acknowledgement row needs a message row from 1 to 2^31 - 1 and a person row from 1 to 2^32 - 1.");

    private static long GroupLeaveProofRow(long groupId, long memberPersonId) =>
        groupId is >= 1 and <= int.MaxValue && memberPersonId is >= 1 and <= uint.MaxValue
            ? (groupId << 32) | memberPersonId
            : throw new ArgumentOutOfRangeException(nameof(groupId), "A group leave proof row needs a group row from 1 to 2^31 - 1 and a person row from 1 to 2^32 - 1.");

    private static long CopiedMessageRow(long conversationId, long position, bool writtenByOwner) =>
        conversationId is >= 1 and <= int.MaxValue && position is >= 1 and <= int.MaxValue
            ? (conversationId << 32) | (writtenByOwner ? 1L << 31 : 0L) | position
            : throw new ArgumentOutOfRangeException(nameof(conversationId), "A copied message row needs a copied conversation row from 1 to 2^31 - 1 and a position from 1 to 2^31 - 1.");
}
