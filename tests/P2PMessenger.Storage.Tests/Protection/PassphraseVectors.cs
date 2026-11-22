namespace P2PMessenger.Storage.Tests.Protection;

internal static class PassphraseVectors
{
    public const string Passphrase = "correct horse battery staple";
    public const string FullWidthPassphrase = "\uff43\uff4f\uff52\uff52\uff45\uff43\uff54\u3000\uff48\uff4f\uff52\uff53\uff45\u3000\uff42\uff41\uff54\uff54\uff45\uff52\uff59\u3000\uff53\uff54\uff41\uff50\uff4c\uff45";
    public const string Salt = "000102030405060708090a0b0c0d0e0f";
    public const string SecondSalt = "101112131415161718191a1b1c1d1e1f";
    public const string Nonce = "a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7";

    public const string Argon2idPasses3Memory64MiB = "0d1a3c6523c8f06e4e0af9c515aa5b5448cfebd6838f2d52c3d8b6ef8ddc3c2e";
    public const string CyrillicPassphraseUtf8 = "d0bfd0b0d180d0bed0bbd18c20d0b4d0bbd18f20d0bfd180d0bed0b2d0b5d180d0bad0b8";
    public const string Argon2idCyrillicPasses1Memory1MiB = "669d56e94c11bc2ba81fb4995e31261074f16c10404102e999e9c9f271a493c0";

    public const string AssociatedData = "5032504d2f646174612d6b65792f706173737068726173652f763101000102030405060708090a0b0c0d0e0f0000040000000002";

    public const string Share = "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f";
    public const string ProtectedShare = "c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3";

    public const string KeyStoreAssociatedData = "5032504d2f646174612d6b65792f706173737068726173652f76320204000102030405060708090a0b0c0d0e0f0000040000000002";

    public const string WrappedWithKeyStore =
        "02040014c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3"
        + "a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7"
        + "4bb21764990b384cb335fac3e102b441ba68a74cd331958cde766b2b56a82c7d"
        + "32127caec7a9ff9bb333bae1d3240021";

    public const string Wrapped =
        "01a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7"
        + "5c0ae5ce673d0dc1e7154289ab002c2f7cfec52406359f7a34303f35c38b53b9"
        + "cca1a58db9500cfbfae2b42cc4a15527";
}
