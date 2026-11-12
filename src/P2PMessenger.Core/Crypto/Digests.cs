using NSec.Cryptography;

namespace P2PMessenger.Core.Crypto;

public static class Digests
{
    public static byte[] Sha3256(ReadOnlySpan<byte> data) => HashAlgorithm.Sha3_256.Hash(data);

    public static byte[] Blake2b256(ReadOnlySpan<byte> data) => HashAlgorithm.Blake2b_256.Hash(data);
}
