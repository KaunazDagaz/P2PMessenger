using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Crypto;

public sealed class X25519KeyPair : IDisposable
{
    public const int PrivateKeyLength = 32;

    private X25519KeyPair(Key key)
    {
        NsecKey = key;
        PublicKey = X25519PublicKey.FromTrustedBytes(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public X25519PublicKey PublicKey { get; }

    internal Key NsecKey { get; }

    public static X25519KeyPair Create() => new(Key.Create(KeyAgreementAlgorithm.X25519, ExportableParameters()));

    public static bool TryImportPrivateKey(ReadOnlySpan<byte> privateKey, [NotNullWhen(true)] out X25519KeyPair? keyPair)
    {
        if (privateKey.Length != PrivateKeyLength
            || !Key.TryImport(KeyAgreementAlgorithm.X25519, privateKey, KeyBlobFormat.RawPrivateKey, out Key? key, ExportableParameters())
            || key is null)
        {
            keyPair = null;
            return false;
        }

        keyPair = new X25519KeyPair(key);
        return true;
    }

    public bool TryExportPrivateKey(Span<byte> destination, out int written) =>
        NsecKey.TryExport(KeyBlobFormat.RawPrivateKey, destination, out written);

    public void Dispose() => NsecKey.Dispose();

    private static KeyCreationParameters ExportableParameters() => new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
}
