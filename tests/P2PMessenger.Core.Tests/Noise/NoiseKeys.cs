using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Tests.Noise;

internal static class NoiseKeys
{
    public static NoiseHash Hash(string name) => name switch
    {
        "BLAKE2b" => NoiseHash.Blake2b,
        "SHA256" => NoiseHash.Sha256,
        "SHA512" => NoiseHash.Sha512,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static X25519KeyPair Private(string hex)
    {
        if (!X25519KeyPair.TryImportPrivateKey(Convert.FromHexString(hex), out var keyPair))
        {
            throw new InvalidOperationException("The test key was not accepted.");
        }

        return keyPair;
    }

    public static X25519PublicKey Public(string hex)
    {
        if (!X25519PublicKey.TryCreate(Convert.FromHexString(hex), out var key))
        {
            throw new InvalidOperationException("The test key was not accepted.");
        }

        return key;
    }
}
