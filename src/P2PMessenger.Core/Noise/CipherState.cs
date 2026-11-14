using System.Buffers.Binary;
using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class CipherState : IDisposable
{
    public const int KeyLength = 32;
    public const int TagLength = 16;

    private const int NonceLength = 12;
    private const ulong ReservedNonce = ulong.MaxValue;

    private Key? _key;
    private ulong _nonce;

    public void InitializeKey(ReadOnlySpan<byte> key)
    {
        _key?.Dispose();
        _key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, key, KeyBlobFormat.RawSymmetricKey);
        _nonce = 0;
    }

    public int EncryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        if (_key is null)
        {
            plaintext.CopyTo(ciphertext);
            return plaintext.Length;
        }

        if (_nonce == ReservedNonce)
        {
            throw new InvalidOperationException("The nonce is used up; the session has to end.");
        }

        Span<byte> nonce = stackalloc byte[NonceLength];
        WriteNonce(_nonce, nonce);
        int length = plaintext.Length + TagLength;
        AeadAlgorithm.ChaCha20Poly1305.Encrypt(_key, nonce, associatedData, plaintext, ciphertext[..length]);
        _nonce++;
        return length;
    }

    public bool TryDecryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int plaintextLength)
    {
        plaintextLength = 0;
        if (_key is null)
        {
            if (ciphertext.Length > plaintext.Length)
            {
                return false;
            }

            ciphertext.CopyTo(plaintext);
            plaintextLength = ciphertext.Length;
            return true;
        }

        if (_nonce == ReservedNonce || ciphertext.Length < TagLength || ciphertext.Length - TagLength > plaintext.Length)
        {
            return false;
        }

        Span<byte> nonce = stackalloc byte[NonceLength];
        WriteNonce(_nonce, nonce);
        Span<byte> output = plaintext[..(ciphertext.Length - TagLength)];
        if (!AeadAlgorithm.ChaCha20Poly1305.Decrypt(_key, nonce, associatedData, ciphertext, output))
        {
            CryptographicOperations.ZeroMemory(output);
            return false;
        }

        _nonce++;
        plaintextLength = output.Length;
        return true;
    }

    public void Dispose()
    {
        _key?.Dispose();
        _key = null;
    }

    private static void WriteNonce(ulong counter, Span<byte> nonce)
    {
        nonce[..4].Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
    }
}
