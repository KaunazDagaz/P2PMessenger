using System.Security.Cryptography;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Identity;

public static class OnionServiceKey
{
    public const int SeedLength = 32;
    public const int ExpandedLength = 64;

    public static bool TryExpand(ReadOnlySpan<byte> seed, Span<byte> destination)
    {
        if (seed.Length != SeedLength || destination.Length < ExpandedLength)
        {
            return false;
        }

        Span<byte> expanded = destination[..ExpandedLength];
        Digests.Sha512(seed, expanded);
        expanded[0] &= 248;
        expanded[31] &= 127;
        expanded[31] |= 64;
        return true;
    }

    internal static bool TryExpandFrom(Ed25519KeyPair keyPair, Span<byte> destination)
    {
        Span<byte> seed = stackalloc byte[SeedLength];
        try
        {
            return keyPair.TryExportSeed(seed, out int written)
                && written == SeedLength
                && TryExpand(seed, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }
    }
}
