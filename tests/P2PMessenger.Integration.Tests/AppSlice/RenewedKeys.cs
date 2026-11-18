using System.Security.Cryptography;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Integration.Tests.AppSlice;

internal static class RenewedKeys
{
    private const int StaticKeyOffset = 33;
    private const int StaticKeyLength = 32;

    public static KeySet WithNewStaticKey(KeySet keys)
    {
        var exported = new byte[KeySet.ExportLength];
        try
        {
            Assert.True(keys.TryExport(exported, out _));
            RandomNumberGenerator.Fill(exported.AsSpan(StaticKeyOffset, StaticKeyLength));
            Assert.True(KeySet.TryImport(exported, out var renewed));
            return renewed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
        }
    }
}
