using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Storage.Repositories;
using P2PMessenger.Storage.Tests.Protection;

namespace P2PMessenger.Storage.Tests.Repositories;

internal static class Samples
{
    public static readonly DateTimeOffset WrittenAt = new(2026, 11, 18, 10, 15, 30, 123, TimeSpan.Zero);

    public static byte[] DataKey() => RecordVectors.Bytes(RecordVectors.DataKey);

    public static DisplayName Name(string text) =>
        DisplayName.TryCreate(text, out var name) ? name : throw new ArgumentException("Not a valid display name.", nameof(text));

    public static ContactCode Code(KeySet keys, string name) => ContactCode.Create(keys, Name(name));

    public static StoredContact Contact(KeySet keys, string name, StoredContactStatus status = StoredContactStatus.WaitingForFirstConnection, bool keyChecked = false) =>
        new(Code(keys, name), ContactChannel.OtherChannel, status, keyChecked);

    public static StoredMessage Message(Ed25519PublicKey contactKey, StoredMessageDirection direction, long sequence, string text, int minute) =>
        new(contactKey, direction, MessageId.Create(), sequence, WrittenAt.AddMinutes(minute), text, WrittenAt.AddMinutes(minute).AddTicks(7));
}
