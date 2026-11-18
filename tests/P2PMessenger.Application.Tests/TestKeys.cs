using System.Security.Cryptography;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Tests;

internal static class TestKeys
{
    private const int IdentityOffset = 1;
    private const int StaticOffset = 33;
    private const int OnionOffset = 65;
    private const int PartLength = 32;

    public static OwnIdentity NewIdentity(string name) => OwnIdentity.Create(Name(name));

    public static DisplayName Name(string name)
    {
        Assert.True(DisplayName.TryCreate(name, out var displayName));
        return displayName;
    }

    public static ContactCode Code(KeySet keys, string name) => ContactCode.Create(keys, Name(name));

    public static KeySet WithNewIdentityKey(KeySet keys) => Changed(keys, IdentityOffset);

    public static KeySet WithNewStaticKey(KeySet keys) => Changed(keys, StaticOffset);

    public static KeySet WithNewOnionKey(KeySet keys) => Changed(keys, OnionOffset);

    private static KeySet Changed(KeySet keys, int offset)
    {
        var exported = new byte[KeySet.ExportLength];
        try
        {
            Assert.True(keys.TryExport(exported, out _));
            RandomNumberGenerator.Fill(exported.AsSpan(offset, PartLength));
            Assert.True(KeySet.TryImport(exported, out var changed));
            return changed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }
    }
}
