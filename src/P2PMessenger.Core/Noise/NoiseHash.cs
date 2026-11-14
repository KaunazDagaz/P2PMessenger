using NSec.Cryptography;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;

namespace P2PMessenger.Core.Noise;

internal sealed class NoiseHash
{
    private const byte InnerPad = 0x36;
    private const byte OuterPad = 0x5C;

    private readonly HashAlgorithm _algorithm;

    private NoiseHash(string name, HashAlgorithm algorithm, int blockLength)
    {
        Name = name;
        _algorithm = algorithm;
        BlockLength = blockLength;
    }

    public static NoiseHash Blake2b { get; } = new("BLAKE2b", HashAlgorithm.Blake2b_512, 128);

    public static NoiseHash Sha256 { get; } = new("SHA256", HashAlgorithm.Sha256, 64);

    public static NoiseHash Sha512 { get; } = new("SHA512", HashAlgorithm.Sha512, 128);

    public string Name { get; }

    public int HashLength => _algorithm.HashSize;

    public int BlockLength { get; }

    public void Hash(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output)
    {
        IncrementalHash.Initialize(_algorithm, out IncrementalHash state);
        IncrementalHash.Update(ref state, first);
        IncrementalHash.Update(ref state, second);
        IncrementalHash.Finalize(ref state, output);
    }

    public void Hmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output)
    {
        if (key.Length > BlockLength)
        {
            throw new ArgumentException("The HMAC key is longer than the hash block.", nameof(key));
        }

        Span<byte> pad = stackalloc byte[BlockLength];
        Span<byte> inner = stackalloc byte[HashLength];
        try
        {
            FillPad(key, InnerPad, pad);
            IncrementalHash.Initialize(_algorithm, out IncrementalHash state);
            IncrementalHash.Update(ref state, pad);
            IncrementalHash.Update(ref state, first);
            IncrementalHash.Update(ref state, second);
            IncrementalHash.Finalize(ref state, inner);

            FillPad(key, OuterPad, pad);
            IncrementalHash.Initialize(_algorithm, out state);
            IncrementalHash.Update(ref state, pad);
            IncrementalHash.Update(ref state, inner);
            IncrementalHash.Finalize(ref state, output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pad);
            CryptographicOperations.ZeroMemory(inner);
        }
    }

    public void Hkdf(ReadOnlySpan<byte> chainingKey, ReadOnlySpan<byte> inputKeyMaterial, Span<byte> output1, Span<byte> output2)
    {
        Span<byte> tempKey = stackalloc byte[HashLength];
        try
        {
            Hmac(chainingKey, inputKeyMaterial, default, tempKey);
            Hmac(tempKey, [0x01], default, output1);
            Hmac(tempKey, output1, [0x02], output2);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempKey);
        }
    }

    private static void FillPad(ReadOnlySpan<byte> key, byte value, Span<byte> pad)
    {
        pad.Fill(value);
        for (int index = 0; index < key.Length; index++)
        {
            pad[index] ^= key[index];
        }
    }
}
