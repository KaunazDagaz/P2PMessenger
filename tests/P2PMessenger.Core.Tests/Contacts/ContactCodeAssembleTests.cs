using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Contacts;

public sealed class ContactCodeAssembleTests
{
    [Theory]
    [Trait("Story", "US-20")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryAssemble_PartsOfAReferenceCode_GivesTheSameBytes(string sample)
    {
        Assert.True(ContactCode.TryParse(CodeSamples.Bytes(sample), out var code, out _));

        var assembled = ContactCode.TryAssemble(
            ContactCode.Version,
            code.IdentityKey,
            code.StaticKey,
            code.OnionAddress,
            code.DisplayName,
            code.Signature,
            out var rebuilt,
            out var error);

        Assert.True(assembled);
        Assert.Equal(ContactCodeError.None, error);
        Assert.Equal(CodeSamples.Bytes(sample), rebuilt!.ToBytes());
    }

    [Fact]
    public void Signature_OfACode_IsItsLast64Bytes()
    {
        using var keys = TestKeys.Alice();
        var code = ContactCode.Create(keys, TestKeys.Name("Alice"));
        var bytes = code.ToBytes();

        Assert.Equal(64, code.Signature.Length);
        Assert.Equal(bytes[^64..], code.Signature.ToArray());
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void TryAssemble_AnotherName_IsRefusedAsDamaged()
    {
        using var keys = TestKeys.Alice();
        var code = ContactCode.Create(keys, TestKeys.Name("Alice"));

        var assembled = ContactCode.TryAssemble(ContactCode.Version, code.IdentityKey, code.StaticKey, code.OnionAddress, TestKeys.Name("Alicia"), code.Signature, out var rebuilt, out var error);

        Assert.False(assembled);
        Assert.Null(rebuilt);
        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void TryAssemble_PartsOfTwoCodes_IsRefusedAsDamaged()
    {
        using var aliceKeys = TestKeys.Alice();
        using var otherKeys = KeySet.Create();
        var alice = ContactCode.Create(aliceKeys, TestKeys.Name("Alice"));

        var assembled = ContactCode.TryAssemble(ContactCode.Version, alice.IdentityKey, otherKeys.StaticKey, alice.OnionAddress, alice.DisplayName, alice.Signature, out _, out var error);

        Assert.False(assembled);
        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void TryAssemble_EveryFlippedBitOfTheSignature_IsRefused()
    {
        using var keys = TestKeys.Alice();
        var code = ContactCode.Create(keys, TestKeys.Name("Alice"));
        var signature = code.Signature.ToArray();

        for (var bit = 0; bit < signature.Length * 8; bit++)
        {
            var changed = (byte[])signature.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(ContactCode.TryAssemble(ContactCode.Version, code.IdentityKey, code.StaticKey, code.OnionAddress, code.DisplayName, changed, out _, out _), $"bit {bit}");
        }
    }

    [Fact]
    public void TryAssemble_AnotherVersion_IsRefusedAsUnsupported()
    {
        using var keys = TestKeys.Alice();
        var code = ContactCode.Create(keys, TestKeys.Name("Alice"));

        var assembled = ContactCode.TryAssemble(2, code.IdentityKey, code.StaticKey, code.OnionAddress, code.DisplayName, code.Signature, out _, out var error);

        Assert.False(assembled);
        Assert.Equal(ContactCodeError.UnsupportedVersion, error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void TryAssemble_SignatureOfAnotherLength_IsRefusedAsDamaged(int length)
    {
        using var keys = TestKeys.Alice();
        var code = ContactCode.Create(keys, TestKeys.Name("Alice"));

        var assembled = ContactCode.TryAssemble(ContactCode.Version, code.IdentityKey, code.StaticKey, code.OnionAddress, code.DisplayName, new byte[length], out _, out var error);

        Assert.False(assembled);
        Assert.Equal(ContactCodeError.Damaged, error);
    }
}
