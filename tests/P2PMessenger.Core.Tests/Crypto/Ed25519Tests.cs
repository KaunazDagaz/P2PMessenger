using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Tests.Crypto;

public sealed class Ed25519Tests
{
    private static readonly Rfc8032Vector[] Vectors =
    [
        new(
            TestKeys.Rfc8032Seed1,
            TestKeys.Rfc8032Public1,
            "",
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"),
        new(
            TestKeys.Rfc8032Seed2,
            TestKeys.Rfc8032Public2,
            "72",
            "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"),
        new(
            TestKeys.Rfc8032Seed3,
            TestKeys.Rfc8032Public3,
            "af82",
            "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a"),
    ];

    public static TheoryData<string, string, string, string> SigningVectors
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var vector in Vectors)
            {
                data.Add(vector.Seed, vector.Public, vector.Message, vector.Signature);
            }

            return data;
        }
    }

    public static TheoryData<string, string, string> VerifyingVectors
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var vector in Vectors)
            {
                data.Add(vector.Public, vector.Message, vector.Signature);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SigningVectors))]
    public void KeyPair_Rfc8032Vector_GivesTheStandardPublicKeyAndSignature(string seedHex, string publicHex, string messageHex, string signatureHex)
    {
        Assert.True(Ed25519KeyPair.TryImportSeed(Convert.FromHexString(seedHex), out var keyPair));
        using (keyPair)
        {
            Assert.Equal(publicHex, Convert.ToHexStringLower(keyPair.PublicKey.Bytes));
            Assert.Equal(signatureHex, Convert.ToHexStringLower(keyPair.Sign(Convert.FromHexString(messageHex))));
        }
    }

    [Theory]
    [MemberData(nameof(VerifyingVectors))]
    public void Verify_Rfc8032Vector_AcceptsTheStandardSignature(string publicHex, string messageHex, string signatureHex)
    {
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(publicHex), out var publicKey));

        Assert.True(publicKey.Verify(Convert.FromHexString(messageHex), Convert.FromHexString(signatureHex)));
    }

    [Theory]
    [MemberData(nameof(VerifyingVectors))]
    public void Verify_AnySingleBitChangeOfSignatureOrMessage_IsRejected(string publicHex, string messageHex, string signatureHex)
    {
        Assert.True(Ed25519PublicKey.TryCreate(Convert.FromHexString(publicHex), out var publicKey));
        var message = Convert.FromHexString(messageHex);
        var signature = Convert.FromHexString(signatureHex);

        for (var bit = 0; bit < signature.Length * 8; bit++)
        {
            Assert.False(publicKey.Verify(message, Flip(signature, bit)), $"signature bit {bit}");
        }

        for (var bit = 0; bit < message.Length * 8; bit++)
        {
            Assert.False(publicKey.Verify(Flip(message, bit), signature), $"message bit {bit}");
        }
    }

    [Fact]
    public void Verify_SignatureOfAnotherKey_IsRejected()
    {
        using var first = Ed25519KeyPair.Create();
        using var second = Ed25519KeyPair.Create();
        var message = "message"u8.ToArray();

        Assert.True(first.PublicKey.Verify(message, first.Sign(message)));
        Assert.False(first.PublicKey.Verify(message, second.Sign(message)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void Verify_SignatureOfWrongLength_IsRejected(int length)
    {
        using var keyPair = Ed25519KeyPair.Create();

        Assert.False(keyPair.PublicKey.Verify("message"u8, new byte[length]));
    }

    [Fact]
    public void Seed_ExportedAfterImport_EqualsTheImportedSeed()
    {
        var seed = Convert.FromHexString(TestKeys.Rfc8032Seed1);
        Assert.True(Ed25519KeyPair.TryImportSeed(seed, out var keyPair));
        using (keyPair)
        {
            var exported = new byte[Ed25519KeyPair.SeedLength];

            Assert.True(keyPair.TryExportSeed(exported, out var written));
            Assert.Equal(Ed25519KeyPair.SeedLength, written);
            Assert.Equal(seed, exported);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Keys_OfWrongLength_AreRejected(int length)
    {
        Assert.False(Ed25519KeyPair.TryImportSeed(new byte[length], out _));
        Assert.False(Ed25519PublicKey.TryCreate(new byte[length], out _));
    }

    [Fact]
    public void Create_TwoKeyPairs_HaveDifferentPublicKeys()
    {
        using var first = Ed25519KeyPair.Create();
        using var second = Ed25519KeyPair.Create();

        Assert.NotEqual(first.PublicKey, second.PublicKey);
    }

    private static byte[] Flip(byte[] data, int bit)
    {
        var copy = (byte[])data.Clone();
        copy[bit / 8] ^= (byte)(1 << (bit % 8));
        return copy;
    }

    private sealed record Rfc8032Vector(string Seed, string Public, string Message, string Signature);
}
