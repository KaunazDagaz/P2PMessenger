using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Crypto;

public sealed class Ed25519KeyPair : IDisposable
{
    public const int SeedLength = 32;
    public const int SignatureLength = 64;

    private readonly Key _key;

    private Ed25519KeyPair(Key key)
    {
        _key = key;
        PublicKey = Ed25519PublicKey.FromTrustedBytes(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public Ed25519PublicKey PublicKey { get; }

    public static Ed25519KeyPair Create() => new(Key.Create(SignatureAlgorithm.Ed25519, ExportableParameters()));

    public static bool TryImportSeed(ReadOnlySpan<byte> seed, [NotNullWhen(true)] out Ed25519KeyPair? keyPair)
    {
        if (seed.Length != SeedLength
            || !Key.TryImport(SignatureAlgorithm.Ed25519, seed, KeyBlobFormat.RawPrivateKey, out Key? key, ExportableParameters())
            || key is null)
        {
            keyPair = null;
            return false;
        }

        keyPair = new Ed25519KeyPair(key);
        return true;
    }

    public bool TryExportSeed(Span<byte> destination, out int written) =>
        _key.TryExport(KeyBlobFormat.RawPrivateKey, destination, out written);

    public void Sign(ReadOnlySpan<byte> message, Span<byte> signature) =>
        SignatureAlgorithm.Ed25519.Sign(_key, message, signature);

    public byte[] Sign(ReadOnlySpan<byte> message) => SignatureAlgorithm.Ed25519.Sign(_key, message);

    public void Dispose() => _key.Dispose();

    private static KeyCreationParameters ExportableParameters() => new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
}
