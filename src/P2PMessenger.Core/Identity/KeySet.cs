using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Identity;

public sealed class KeySet : IDisposable
{
    public const int ExportLength = 1 + Ed25519KeyPair.SeedLength + X25519KeyPair.PrivateKeyLength + Ed25519KeyPair.SeedLength;

    private const byte ExportVersion = 1;
    private const int IdentityOffset = 1;
    private const int StaticOffset = IdentityOffset + Ed25519KeyPair.SeedLength;
    private const int OnionOffset = StaticOffset + X25519KeyPair.PrivateKeyLength;

    private KeySet(Ed25519KeyPair identity, X25519KeyPair staticKey, Ed25519KeyPair onion)
    {
        IdentityKeyPair = identity;
        StaticKeyPair = staticKey;
        OnionKeyPair = onion;
        OnionAddress = OnionAddress.FromPublicKey(onion.PublicKey);
        Fingerprint = Fingerprint.Of(identity.PublicKey);
    }

    public Ed25519PublicKey IdentityKey => IdentityKeyPair.PublicKey;

    public X25519PublicKey StaticKey => StaticKeyPair.PublicKey;

    public Ed25519PublicKey OnionKey => OnionKeyPair.PublicKey;

    public OnionAddress OnionAddress { get; }

    public Fingerprint Fingerprint { get; }

    internal Ed25519KeyPair IdentityKeyPair { get; }

    internal X25519KeyPair StaticKeyPair { get; }

    internal Ed25519KeyPair OnionKeyPair { get; }

    public static KeySet Create() => new(Ed25519KeyPair.Create(), X25519KeyPair.Create(), Ed25519KeyPair.Create());

    public static bool TryImport(ReadOnlySpan<byte> exported, [NotNullWhen(true)] out KeySet? keySet)
    {
        keySet = null;

        if (exported.Length != ExportLength || exported[0] != ExportVersion)
        {
            return false;
        }

        ReadOnlySpan<byte> identitySeed = exported.Slice(IdentityOffset, Ed25519KeyPair.SeedLength);
        ReadOnlySpan<byte> staticPrivateKey = exported.Slice(StaticOffset, X25519KeyPair.PrivateKeyLength);
        ReadOnlySpan<byte> onionSeed = exported.Slice(OnionOffset, Ed25519KeyPair.SeedLength);

        if (identitySeed.SequenceEqual(onionSeed)
            || !Ed25519KeyPair.TryImportSeed(identitySeed, out Ed25519KeyPair? identity))
        {
            return false;
        }

        if (!X25519KeyPair.TryImportPrivateKey(staticPrivateKey, out X25519KeyPair? staticKey))
        {
            identity.Dispose();
            return false;
        }

        if (!Ed25519KeyPair.TryImportSeed(onionSeed, out Ed25519KeyPair? onion))
        {
            identity.Dispose();
            staticKey.Dispose();
            return false;
        }

        keySet = new KeySet(identity, staticKey, onion);
        return true;
    }

    public bool TryExport(Span<byte> destination, out int written)
    {
        written = 0;

        if (destination.Length < ExportLength)
        {
            return false;
        }

        Span<byte> target = destination[..ExportLength];
        target[0] = ExportVersion;

        bool exported = IdentityKeyPair.TryExportSeed(target.Slice(IdentityOffset, Ed25519KeyPair.SeedLength), out int identityLength)
            && identityLength == Ed25519KeyPair.SeedLength
            && StaticKeyPair.TryExportPrivateKey(target.Slice(StaticOffset, X25519KeyPair.PrivateKeyLength), out int staticLength)
            && staticLength == X25519KeyPair.PrivateKeyLength
            && OnionKeyPair.TryExportSeed(target.Slice(OnionOffset, Ed25519KeyPair.SeedLength), out int onionLength)
            && onionLength == Ed25519KeyPair.SeedLength;

        if (!exported)
        {
            CryptographicOperations.ZeroMemory(target);
            return false;
        }

        written = ExportLength;
        return true;
    }

    public void Dispose()
    {
        IdentityKeyPair.Dispose();
        StaticKeyPair.Dispose();
        OnionKeyPair.Dispose();
    }
}
