using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;
using P2PMessenger.Core.Crypto;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Sessions;

internal static class PairwiseKeys
{
    private static ReadOnlySpan<byte> Salt => "P2PM/pairwise/v1"u8;

    private static ReadOnlySpan<byte> InfoLabel => "P2PM/pairwise-key/v1"u8;

    public static bool TryDerive(X25519KeyPair own, X25519PublicKey contact, [NotNullWhen(true)] out Key? pairwiseKey)
    {
        pairwiseKey = null;
        if (!contact.TryImportNsec(out PublicKey? contactKey))
        {
            return false;
        }

        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(own.NsecKey, contactKey);
        if (sharedSecret is null)
        {
            return false;
        }

        ReadOnlySpan<byte> ownPublicKey = own.PublicKey.Bytes;
        ReadOnlySpan<byte> contactPublicKey = contact.Bytes;
        bool ownIsLower = ownPublicKey.SequenceCompareTo(contactPublicKey) <= 0;
        Span<byte> info = stackalloc byte[InfoLabel.Length + (2 * X25519PublicKey.Length)];
        InfoLabel.CopyTo(info);
        (ownIsLower ? ownPublicKey : contactPublicKey).CopyTo(info[InfoLabel.Length..]);
        (ownIsLower ? contactPublicKey : ownPublicKey).CopyTo(info[(InfoLabel.Length + X25519PublicKey.Length)..]);

        HkdfSha256 hkdf = KeyDerivationAlgorithm.HkdfSha256;
        byte[] pseudorandomKey = hkdf.Extract(sharedSecret, Salt);
        try
        {
            pairwiseKey = hkdf.ExpandKey(pseudorandomKey, info, MacAlgorithm.Blake2b_128);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudorandomKey);
        }
    }
}
