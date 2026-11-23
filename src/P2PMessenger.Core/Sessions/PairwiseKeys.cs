using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;
using P2PMessenger.Core.Crypto;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Sessions;

internal static class PairwiseKeys
{
    private static ReadOnlySpan<byte> Salt => "P2PM/pairwise/v1"u8;

    private static ReadOnlySpan<byte> HintInfoLabel => "P2PM/pairwise-key/v1"u8;

    private static ReadOnlySpan<byte> AnnouncementInfoLabel => "P2PM/announcement-key/v1"u8;

    public static bool TryDerive(
        X25519KeyPair own,
        X25519PublicKey contact,
        [NotNullWhen(true)] out Key? hintKey,
        [NotNullWhen(true)] out Key? announcementKey)
    {
        hintKey = null;
        announcementKey = null;
        if (!contact.TryImportNsec(out PublicKey? contactKey))
        {
            return false;
        }

        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(own.NsecKey, contactKey);
        if (sharedSecret is null)
        {
            return false;
        }

        HkdfSha256 hkdf = KeyDerivationAlgorithm.HkdfSha256;
        byte[] pseudorandomKey = hkdf.Extract(sharedSecret, Salt);
        try
        {
            hintKey = Expand(hkdf, pseudorandomKey, HintInfoLabel, own.PublicKey, contact);
            announcementKey = Expand(hkdf, pseudorandomKey, AnnouncementInfoLabel, own.PublicKey, contact);
            return true;
        }
        catch
        {
            hintKey?.Dispose();
            hintKey = null;
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudorandomKey);
        }
    }

    private static Key Expand(HkdfSha256 hkdf, byte[] pseudorandomKey, ReadOnlySpan<byte> label, X25519PublicKey own, X25519PublicKey contact)
    {
        ReadOnlySpan<byte> ownPublicKey = own.Bytes;
        ReadOnlySpan<byte> contactPublicKey = contact.Bytes;
        bool ownIsLower = ownPublicKey.SequenceCompareTo(contactPublicKey) <= 0;
        Span<byte> info = stackalloc byte[label.Length + (2 * X25519PublicKey.Length)];
        label.CopyTo(info);
        (ownIsLower ? ownPublicKey : contactPublicKey).CopyTo(info[label.Length..]);
        (ownIsLower ? contactPublicKey : ownPublicKey).CopyTo(info[(label.Length + X25519PublicKey.Length)..]);
        return hkdf.ExpandKey(pseudorandomKey, info, MacAlgorithm.Blake2b_128);
    }
}
