using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class SymmetricState : IDisposable
{
    private readonly NoiseHash hash;
    private readonly byte[] chainingKey;
    private readonly byte[] handshakeHash;
    private readonly CipherState cipher = new();

    public SymmetricState(NoiseHash hash, ReadOnlySpan<byte> protocolName)
    {
        this.hash = hash;
        this.chainingKey = GC.AllocateArray<byte>(hash.HashLength, pinned: true);
        this.handshakeHash = GC.AllocateArray<byte>(hash.HashLength, pinned: true);
        if (protocolName.Length <= hash.HashLength)
        {
            protocolName.CopyTo(this.handshakeHash);
        }
        else
        {
            hash.Hash(protocolName, default, this.handshakeHash);
        }

        this.handshakeHash.CopyTo(this.chainingKey, 0);
    }

    public ReadOnlySpan<byte> HandshakeHash => handshakeHash;

    public void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
    {
        Span<byte> tempKey = stackalloc byte[hash.HashLength];
        try
        {
            hash.Hkdf(chainingKey, inputKeyMaterial, chainingKey, tempKey);
            cipher.InitializeKey(tempKey[..CipherState.KeyLength]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempKey);
        }
    }

    public void MixKeyAndHash(ReadOnlySpan<byte> inputKeyMaterial)
    {
        Span<byte> tempHash = stackalloc byte[hash.HashLength];
        Span<byte> tempKey = stackalloc byte[hash.HashLength];
        try
        {
            hash.Hkdf(chainingKey, inputKeyMaterial, chainingKey, tempHash, tempKey);
            MixHash(tempHash);
            cipher.InitializeKey(tempKey[..CipherState.KeyLength]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempHash);
            CryptographicOperations.ZeroMemory(tempKey);
        }
    }

    public void MixHash(ReadOnlySpan<byte> data)
    {
        hash.Hash(handshakeHash, data, handshakeHash);
    }

    public int EncryptAndHash(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        int length = cipher.EncryptWithAd(handshakeHash, plaintext, ciphertext);
        MixHash(ciphertext[..length]);
        return length;
    }

    public bool TryDecryptAndHash(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int plaintextLength)
    {
        if (!cipher.TryDecryptWithAd(handshakeHash, ciphertext, plaintext, out plaintextLength))
        {
            return false;
        }

        MixHash(ciphertext);
        return true;
    }

    public (CipherState First, CipherState Second) Split()
    {
        Span<byte> first = stackalloc byte[hash.HashLength];
        Span<byte> second = stackalloc byte[hash.HashLength];
        CipherState? initiatorToResponder = null;
        try
        {
            hash.Hkdf(chainingKey, default, first, second);
            initiatorToResponder = new CipherState();
            initiatorToResponder.InitializeKey(first[..CipherState.KeyLength]);
            CipherState responderToInitiator = new();
            responderToInitiator.InitializeKey(second[..CipherState.KeyLength]);
            (CipherState, CipherState) result = (initiatorToResponder, responderToInitiator);
            initiatorToResponder = null;
            return result;
        }
        finally
        {
            initiatorToResponder?.Dispose();
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(second);
        }
    }

    public void Dispose()
    {
        cipher.Dispose();
        CryptographicOperations.ZeroMemory(chainingKey);
        CryptographicOperations.ZeroMemory(handshakeHash);
    }
}
