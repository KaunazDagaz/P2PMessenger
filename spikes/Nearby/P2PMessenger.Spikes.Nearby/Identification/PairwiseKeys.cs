using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Spikes.Nearby.Identification;

public static class PairwiseKeys
{
    public const int PublicKeyLength = 32;

    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("P2PM/s05/pairwise-salt/v1");
    private static readonly byte[] InfoLabel = Encoding.ASCII.GetBytes("P2PM/s05/pairwise-mac/v1");

    public static Key DeriveMacKey(Key staticKey, ReadOnlySpan<byte> ownPublicKey, ReadOnlySpan<byte> contactPublicKey)
    {
        if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, contactPublicKey, KeyBlobFormat.RawPublicKey, out PublicKey? contact) || contact is null)
        {
            throw new CryptographicException("The contact's public key is not valid.");
        }

        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(staticKey, contact)
            ?? throw new CryptographicException("The key agreement gave an all-zero result.");
        HkdfSha256 hkdf = KeyDerivationAlgorithm.HkdfSha256;
        byte[] pseudorandomKey = hkdf.Extract(sharedSecret, Salt);
        try
        {
            byte[] info = Info(ownPublicKey, contactPublicKey);
            return hkdf.ExpandKey(pseudorandomKey, info, MacAlgorithm.Blake2b_128);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudorandomKey);
        }
    }

    public static byte[] ExportStaticDh(Key staticKey, ReadOnlySpan<byte> contactPublicKey)
    {
        if (!PublicKey.TryImport(KeyAgreementAlgorithm.X25519, contactPublicKey, KeyBlobFormat.RawPublicKey, out PublicKey? contact) || contact is null)
        {
            throw new CryptographicException("The contact's public key is not valid.");
        }

        SharedSecretCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(staticKey, contact, in parameters)
            ?? throw new CryptographicException("The key agreement gave an all-zero result.");
        byte[] raw = GC.AllocateArray<byte>(PublicKeyLength, pinned: true);
        if (!sharedSecret.TryExport(SharedSecretBlobFormat.RawSharedSecret, raw, out int written) || written != PublicKeyLength)
        {
            throw new CryptographicException("The X25519 output could not be exported.");
        }

        return raw;
    }

    private static byte[] Info(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        bool firstIsLower = first.SequenceCompareTo(second) <= 0;
        byte[] info = new byte[InfoLabel.Length + (2 * PublicKeyLength)];
        InfoLabel.CopyTo(info, 0);
        (firstIsLower ? first : second).CopyTo(info.AsSpan(InfoLabel.Length));
        (firstIsLower ? second : first).CopyTo(info.AsSpan(InfoLabel.Length + PublicKeyLength));
        return info;
    }
}
