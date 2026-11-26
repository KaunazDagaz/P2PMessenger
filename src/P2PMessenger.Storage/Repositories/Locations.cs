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

    public static RecordLocation GroupKey(long groupId, long epoch) => new("group_key", "key_enc", GroupKeyRow(groupId, epoch));

    public static RecordLocation GroupRecord(long groupId, long epoch) => new("group_key", "record_enc", GroupKeyRow(groupId, epoch));

    private static long GroupKeyRow(long groupId, long epoch) =>
        groupId is >= 1 and <= int.MaxValue && epoch is >= 1 and <= uint.MaxValue
            ? (groupId << 32) | epoch
            : throw new ArgumentOutOfRangeException(nameof(groupId), "A group key row needs a group row from 1 to 2^31 - 1 and an epoch from 1 to 2^32 - 1.");
}
