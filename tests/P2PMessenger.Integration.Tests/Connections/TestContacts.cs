using System.Security.Cryptography;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;
using P2PMessenger.Core.Sessions;

namespace P2PMessenger.Integration.Tests.Connections;

internal static class TestContacts
{
    private const int IdentitySeedOffset = 1;
    private const int IdentitySeedLength = 32;

    public static SessionContact Create(KeySet own, KeySet other)
    {
        if (!SessionContact.TryCreate(own, other.IdentityKey, other.StaticKey, out var contact))
        {
            throw new InvalidOperationException("The test contact was not accepted.");
        }

        return contact;
    }

    public static KeySet WithAnotherIdentityKey(KeySet keys)
    {
        var exported = new byte[KeySet.ExportLength];
        try
        {
            Assert.True(keys.TryExport(exported, out _));
            RandomNumberGenerator.Fill(exported.AsSpan(IdentitySeedOffset, IdentitySeedLength));
            Assert.True(KeySet.TryImport(exported, out var changed));
            return changed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }
    }

    public static KeySet CreateWithStaticKeyOrder(KeySet other, bool lower)
    {
        while (true)
        {
            var keys = KeySet.Create();
            var isLower = keys.StaticKey.Bytes.SequenceCompareTo(other.StaticKey.Bytes) < 0;
            if (isLower == lower)
            {
                return keys;
            }

            keys.Dispose();
        }
    }

    public static TextMessage Text(long sequence, string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence), text, out var message));
        return message;
    }
}
