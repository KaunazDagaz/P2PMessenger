using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Crypto;

internal static class X25519Agreement
{
    public const int OutputLength = 32;

    public static bool TryAgree(Key privateKey, PublicKey publicKey, Span<byte> output)
    {
        if (output.Length != OutputLength)
        {
            throw new ArgumentException("The output must be 32 bytes long.", nameof(output));
        }

        SharedSecretCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(privateKey, publicKey, in parameters);
        if (sharedSecret is null)
        {
            return false;
        }

        if (!sharedSecret.TryExport(SharedSecretBlobFormat.RawSharedSecret, output, out int written) || written != OutputLength)
        {
            CryptographicOperations.ZeroMemory(output);
            return false;
        }

        return true;
    }
}
