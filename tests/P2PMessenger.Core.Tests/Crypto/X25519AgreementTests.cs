using NSec.Cryptography;
using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Tests.Crypto;

public sealed class X25519AgreementTests
{
    private const string Rfc7748SharedSecret = "4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742";

    [Fact]
    public void TryAgree_Rfc7748Vector_ExportsTheStandardSharedSecret()
    {
        using var alice = Key.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(TestKeys.Rfc7748AlicePrivate), KeyBlobFormat.RawPrivateKey);
        var bob = PublicKey.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(TestKeys.Rfc7748BobPublic), KeyBlobFormat.RawPublicKey);
        var output = new byte[X25519Agreement.OutputLength];

        Assert.True(X25519Agreement.TryAgree(alice, bob, output));

        Assert.Equal(Rfc7748SharedSecret, Convert.ToHexStringLower(output));
    }

    [Fact]
    public void TryAgree_BothDirections_GiveTheSameSecret()
    {
        using var alice = Key.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(TestKeys.Rfc7748AlicePrivate), KeyBlobFormat.RawPrivateKey);
        using var bob = Key.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(TestKeys.Rfc7748BobPrivate), KeyBlobFormat.RawPrivateKey);
        var aliceOutput = new byte[X25519Agreement.OutputLength];
        var bobOutput = new byte[X25519Agreement.OutputLength];

        Assert.True(X25519Agreement.TryAgree(alice, bob.PublicKey, aliceOutput));
        Assert.True(X25519Agreement.TryAgree(bob, alice.PublicKey, bobOutput));

        Assert.Equal(Rfc7748SharedSecret, Convert.ToHexStringLower(bobOutput));
        Assert.Equal(Convert.ToHexStringLower(aliceOutput), Convert.ToHexStringLower(bobOutput));
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800")]
    public void TryAgree_PublicKeyOfSmallOrder_IsRefusedAndLeavesNoOutput(string hex)
    {
        using var alice = Key.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(TestKeys.Rfc7748AlicePrivate), KeyBlobFormat.RawPrivateKey);
        var smallOrder = PublicKey.Import(KeyAgreementAlgorithm.X25519, Convert.FromHexString(hex), KeyBlobFormat.RawPublicKey);
        var output = Enumerable.Repeat((byte)0xAA, X25519Agreement.OutputLength).ToArray();

        Assert.False(X25519Agreement.TryAgree(alice, smallOrder, output));

        Assert.All(output, value => Assert.Equal((byte)0xAA, value));
    }
}
