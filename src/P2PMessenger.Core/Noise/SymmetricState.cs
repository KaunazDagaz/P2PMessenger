using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class SymmetricState : IDisposable
{
    private readonly NoiseHash _hash;
    private readonly byte[] _chainingKey;
    private readonly byte[] _handshakeHash;
    private readonly CipherState _cipher = new();

    public SymmetricState(NoiseHash hash, ReadOnlySpan<byte> protocolName)
    {
        _hash = hash;
        _chainingKey = GC.AllocateArray<byte>(hash.HashLength, pinned: true);
        _handshakeHash = GC.AllocateArray<byte>(hash.HashLength, pinned: true);
        if (protocolName.Length <= hash.HashLength)
        {
            protocolName.CopyTo(_handshakeHash);
        }
        else
        {
            hash.Hash(protocolName, default, _handshakeHash);
        }

        _handshakeHash.CopyTo(_chainingKey, 0);
    }

    public ReadOnlySpan<byte> HandshakeHash => _handshakeHash;

    public void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
    {
        Span<byte> tempKey = stackalloc byte[_hash.HashLength];
        try
        {
            _hash.Hkdf(_chainingKey, inputKeyMaterial, _chainingKey, tempKey);
            _cipher.InitializeKey(tempKey[..CipherState.KeyLength]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempKey);
        }
    }

    public void MixHash(ReadOnlySpan<byte> data)
    {
        _hash.Hash(_handshakeHash, data, _handshakeHash);
    }

    public int EncryptAndHash(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        int length = _cipher.EncryptWithAd(_handshakeHash, plaintext, ciphertext);
        MixHash(ciphertext[..length]);
        return length;
    }

    public bool TryDecryptAndHash(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int plaintextLength)
    {
        if (!_cipher.TryDecryptWithAd(_handshakeHash, ciphertext, plaintext, out plaintextLength))
        {
            return false;
        }

        MixHash(ciphertext);
        return true;
    }

    public (CipherState First, CipherState Second) Split()
    {
        Span<byte> first = stackalloc byte[_hash.HashLength];
        Span<byte> second = stackalloc byte[_hash.HashLength];
        CipherState? initiatorToResponder = null;
        try
        {
            _hash.Hkdf(_chainingKey, default, first, second);
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
        _cipher.Dispose();
        CryptographicOperations.ZeroMemory(_chainingKey);
        CryptographicOperations.ZeroMemory(_handshakeHash);
    }
}
