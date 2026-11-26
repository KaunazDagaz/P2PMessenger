using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

internal static class GroupSamples
{
    public static GroupId VectorGroupId() => GroupId.TryCreate(Convert.FromHexString(GroupVectors.GroupId), out var id) ? id : throw new InvalidOperationException();

    public static GroupKey VectorKey() => GroupKey.TryImport(Convert.FromHexString(GroupVectors.Key), out var key) ? key : throw new InvalidOperationException();

    public static Ed25519PublicKey Key(string hex) => Ed25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key) ? key : throw new InvalidOperationException();

    public static GroupMember[] VectorMembers() =>
    [
        new(Key(TestKeys.Rfc8032Public1), TestKeys.Name("Alice")),
        new(Key(TestKeys.Rfc8032Public3), TestKeys.Name("Bob")),
        new(Key(TestKeys.Rfc8032Public2), TestKeys.Name("Carol")),
    ];

    public static GroupRecord VectorRecord(KeySet alice, GroupKey key) =>
        GroupRecord.Create(VectorGroupId(), 1, TestKeys.Name("Field team"), key, VectorMembers(), alice);

    public static GroupText VectorText() =>
        GroupText.TryCreate(
            MessageId.TryCreate(Convert.FromHexString(GroupVectors.MessageId), out var id) ? id : throw new InvalidOperationException(),
            1,
            DateTimeOffset.FromUnixTimeMilliseconds(GroupVectors.WrittenAtMilliseconds),
            GroupVectors.Text,
            out var text)
            ? text
            : throw new InvalidOperationException();

    public static GroupMessage VectorMessage(KeySet alice, GroupKey key) =>
        GroupMessage.Seal(key, VectorGroupId(), 1, alice, VectorText(), Convert.FromHexString(GroupVectors.Nonce));

    public static GroupText Text(long sequence, string text) =>
        GroupText.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch, text, out var groupText) ? groupText : throw new InvalidOperationException();
}
