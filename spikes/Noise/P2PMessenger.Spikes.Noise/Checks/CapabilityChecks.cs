using System.Reflection;
using NSec.Cryptography;
using P2PMessenger.Spikes.Noise.Own;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class CapabilityChecks
{
    private static readonly byte[] AlicePrivateKey = Convert.FromHexString("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
    private static readonly byte[] BobPublicKey = Convert.FromHexString("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");
    private static readonly byte[] SharedSecretValue = Convert.FromHexString("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742");
    private static readonly byte[] Sha3OfAbc = Convert.FromHexString("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532");
    private static readonly int[] HmacDataLengths = [0, 1, 63, 64, 65, 127, 128, 129, 1000];

    public static void Run(CheckReport report)
    {
        report.Run("NSec gives the raw X25519 output (RFC 7748, section 6.1)", RawX25519Output);
        report.Run("NSec keeps a shared secret unexportable by default", DefaultSharedSecretIsNotExportable);
        report.Run("NSec's MAC algorithms", MacAlgorithms);
        report.Run("own HMAC on NSec's SHA-256 and SHA-512 equals NSec's HMAC", OwnHmacMatchesNSec);
        report.Run("NSec's SHA3-256 (FIPS 202), for the onion address in S08", Sha3);
    }

    private static CheckOutcome RawX25519Output()
    {
        using Key alice = Key.Import(KeyAgreementAlgorithm.X25519, AlicePrivateKey, KeyBlobFormat.RawPrivateKey);
        PublicKey bob = PublicKey.Import(KeyAgreementAlgorithm.X25519, BobPublicKey, KeyBlobFormat.RawPublicKey);
        SharedSecretCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(alice, bob, in parameters);
        if (sharedSecret is null)
        {
            return CheckOutcome.Fail("the key agreement returned no shared secret");
        }

        byte[] output = sharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret);
        return output.AsSpan().SequenceEqual(SharedSecretValue)
            ? CheckOutcome.Pass("SharedSecret.Export(RawSharedSecret) gives the RFC value when the shared secret is created with KeyExportPolicies.AllowPlaintextExport")
            : CheckOutcome.Fail("the exported value differs from the RFC value");
    }

    private static CheckOutcome DefaultSharedSecretIsNotExportable()
    {
        using Key alice = Key.Import(KeyAgreementAlgorithm.X25519, AlicePrivateKey, KeyBlobFormat.RawPrivateKey);
        PublicKey bob = PublicKey.Import(KeyAgreementAlgorithm.X25519, BobPublicKey, KeyBlobFormat.RawPublicKey);
        using SharedSecret? sharedSecret = KeyAgreementAlgorithm.X25519.Agree(alice, bob);
        if (sharedSecret is null)
        {
            return CheckOutcome.Fail("the key agreement returned no shared secret");
        }

        try
        {
            _ = sharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret);
            return CheckOutcome.Fail("the shared secret was exported without an export policy");
        }
        catch (InvalidOperationException)
        {
            return CheckOutcome.Pass("Export throws InvalidOperationException unless the export policy allows it");
        }
    }

    private static CheckOutcome MacAlgorithms()
    {
        string[] names = [.. typeof(MacAlgorithm)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)];
        bool hmacBlake2b = names.Any(name => name.Contains("Hmac", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Blake", StringComparison.OrdinalIgnoreCase));
        string offered = string.Join(", ", names);
        return hmacBlake2b
            ? CheckOutcome.Info("HMAC-BLAKE2b is offered: " + offered)
            : CheckOutcome.Info("no HMAC-BLAKE2b; offered: " + offered + " (Blake2b_* is keyed BLAKE2b, not HMAC)");
    }

    private static CheckOutcome OwnHmacMatchesNSec()
    {
        (NoiseHashFunction Ours, MacAlgorithm Theirs)[] pairs =
        [
            (NoiseHashFunction.Sha256, MacAlgorithm.HmacSha256),
            (NoiseHashFunction.Sha512, MacAlgorithm.HmacSha512),
        ];

        int compared = 0;
        foreach ((NoiseHashFunction ours, MacAlgorithm theirs) in pairs)
        {
            foreach (int length in HmacDataLengths)
            {
                byte[] keyBytes = RandomNumberGenerator.GetBytes(ours.HashLength);
                byte[] data = RandomNumberGenerator.GetBytes(length);
                using Key key = Key.Import(theirs, keyBytes, KeyBlobFormat.RawSymmetricKey);
                byte[] expected = theirs.Mac(key, data);
                byte[] actual = new byte[ours.HashLength];
                ours.Hmac(keyBytes, data, default, actual);
                if (!actual.AsSpan().SequenceEqual(expected))
                {
                    return CheckOutcome.Fail($"HMAC-{ours.Name} differs for {length} bytes of data");
                }

                compared++;
            }
        }

        return CheckOutcome.Pass($"{compared} random keys and messages of 0 to 1000 bytes; the same code computes HMAC-BLAKE2b");
    }

    private static CheckOutcome Sha3()
    {
        byte[] digest = HashAlgorithm.Sha3_256.Hash("abc"u8);
        return digest.AsSpan().SequenceEqual(Sha3OfAbc)
            ? CheckOutcome.Pass("SHA3-256(\"abc\") equals the FIPS 202 value")
            : CheckOutcome.Fail("SHA3-256(\"abc\") differs from the FIPS 202 value");
    }
}
