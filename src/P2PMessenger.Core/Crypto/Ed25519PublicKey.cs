using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Crypto;

public sealed class Ed25519PublicKey : IEquatable<Ed25519PublicKey>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private Ed25519PublicKey(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> Bytes => _bytes;

    public static bool TryCreate(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out Ed25519PublicKey? key)
    {
        if (bytes.Length != Length)
        {
            key = null;
            return false;
        }

        key = new Ed25519PublicKey(bytes.ToArray());
        return true;
    }

    public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != Ed25519KeyPair.SignatureLength
            || !PublicKey.TryImport(SignatureAlgorithm.Ed25519, _bytes, KeyBlobFormat.RawPublicKey, out PublicKey? key)
            || key is null)
        {
            return false;
        }

        return SignatureAlgorithm.Ed25519.Verify(key, message, signature);
    }

    public bool Equals(Ed25519PublicKey? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is Ed25519PublicKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    internal static Ed25519PublicKey FromTrustedBytes(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());
}
