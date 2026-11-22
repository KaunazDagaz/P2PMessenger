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
}
