namespace P2PMessenger.Storage.Protection;

public sealed class PassphraseDerivation
{
    public const int SaltLength = 16;

    public PassphraseDerivation(ReadOnlySpan<byte> salt, PassphraseKdf kdf)
    {
        if (salt.Length != SaltLength)
        {
            throw new ArgumentException("The salt must be 16 bytes long.", nameof(salt));
        }

        if (!kdf.IsAcceptable)
        {
            throw new ArgumentOutOfRangeException(nameof(kdf), kdf, "The key-derivation parameters are outside the accepted range.");
        }

        Salt = salt.ToArray();
        Kdf = kdf;
    }

    public ReadOnlyMemory<byte> Salt { get; }

    public PassphraseKdf Kdf { get; }
}
