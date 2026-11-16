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

    private Key? key;
    private ulong nonce;
    private bool disposed;

    public void InitializeKey(ReadOnlySpan<byte> keyBytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        key?.Dispose();
        key = Key.Import(AeadAlgorithm.ChaCha20Poly1305, keyBytes, KeyBlobFormat.RawSymmetricKey);
        nonce = 0;
    }

    public int EncryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Key? currentKey = key;
        if (currentKey is null)
        {
            plaintext.CopyTo(ciphertext);
            return plaintext.Length;
        }

        if (nonce == ReservedNonce)
        {
            throw new InvalidOperationException("The nonce is used up; the session has to end.");
        }

        Span<byte> nonceBytes = stackalloc byte[NonceLength];
        WriteNonce(nonce, nonceBytes);
        int length = plaintext.Length + TagLength;
        AeadAlgorithm.ChaCha20Poly1305.Encrypt(currentKey, nonceBytes, associatedData, plaintext, ciphertext[..length]);
        nonce++;
        return length;
    }

    public bool TryDecryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int plaintextLength)
    {
        plaintextLength = 0;
        if (disposed)
        {
            return false;
        }

        Key? currentKey = key;
        if (currentKey is null)
        {
            if (ciphertext.Length > plaintext.Length)
            {
                return false;
            }

            ciphertext.CopyTo(plaintext);
            plaintextLength = ciphertext.Length;
            return true;
        }

        if (nonce == ReservedNonce || ciphertext.Length < TagLength || ciphertext.Length - TagLength > plaintext.Length)
        {
            return false;
        }

        Span<byte> nonceBytes = stackalloc byte[NonceLength];
        WriteNonce(nonce, nonceBytes);
        Span<byte> output = plaintext[..(ciphertext.Length - TagLength)];
        if (!AeadAlgorithm.ChaCha20Poly1305.Decrypt(currentKey, nonceBytes, associatedData, ciphertext, output))
        {
            CryptographicOperations.ZeroMemory(output);
            return false;
        }

        nonce++;
        plaintextLength = output.Length;
        return true;
    }

    public void Dispose()
    {
        disposed = true;
        key?.Dispose();
    }

    private static void WriteNonce(ulong counter, Span<byte> nonce)
    {
        nonce[..4].Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
    }
}
