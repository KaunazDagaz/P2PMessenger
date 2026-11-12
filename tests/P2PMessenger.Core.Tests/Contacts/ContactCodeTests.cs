using System.Security.Cryptography;
using System.Text;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Contacts;

public sealed class ContactCodeTests
{
    [Theory]
    [Trait("Story", "US-02")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void Create_ReferenceKeys_GivesTheCodeOfTheIndependentImplementation(string sample)
    {
        var code = CodeSamples.Create(sample);

        Assert.Equal(CodeSamples.Hex(sample), Convert.ToHexStringLower(code.ToBytes()));
        Assert.Equal(CodeSamples.Text(sample), code.ToText());
    }

    [Theory]
    [Trait("Story", "US-02")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParse_ReferenceCode_GivesTheKeysAndTheName(string sample)
    {
        using var keys = CodeSamples.Keys(sample);

        Assert.True(ContactCode.TryParse(CodeSamples.Bytes(sample), out var code, out var error));

        Assert.Equal(ContactCodeError.None, error);
        Assert.Equal(keys.IdentityKey, code.IdentityKey);
        Assert.Equal(keys.StaticKey, code.StaticKey);
        Assert.Equal(keys.OnionAddress, code.OnionAddress);
        Assert.Equal(keys.Fingerprint, code.Fingerprint);
        Assert.Equal(CodeSamples.DisplayText(sample), code.DisplayName.Value);
        Assert.Equal(CodeSamples.Bytes(sample), code.ToBytes());
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Create_ThenParse_RoundTripsForFreshKeys()
    {
        for (var count = 0; count < 20; count++)
        {
            using var keys = KeySet.Create();
            var created = ContactCode.Create(keys, TestKeys.Name($"Friend {count}"));

            Assert.True(ContactCode.TryParse(created.ToBytes(), out var parsed, out _));
            Assert.Equal(created, parsed);
            Assert.True(ContactCode.TryParseText(created.ToText(), out var parsedText, out _));
            Assert.Equal(created, parsedText);
        }
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Create_SameKeysAndNameTwice_GivesTheSameCode()
    {
        using var keys = TestKeys.Alice();

        var first = ContactCode.Create(keys, TestKeys.Name("Alice"));
        var second = ContactCode.Create(keys, TestKeys.Name("Alice"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.ToText(), second.ToText());
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Create_SameKeysAnotherName_GivesAnotherCodeWithTheSameKeys()
    {
        using var keys = TestKeys.Alice();

        var first = ContactCode.Create(keys, TestKeys.Name("Alice"));
        var second = ContactCode.Create(keys, TestKeys.Name("Alice B."));

        Assert.NotEqual(first, second);
        Assert.Equal(first.IdentityKey, second.IdentityKey);
        Assert.Equal(first.StaticKey, second.StaticKey);
        Assert.Equal(first.OnionAddress, second.OnionAddress);
    }

    [Theory]
    [Trait("Story", "US-01")]
    [Trait("NFR", "NFR-10")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void Code_Bytes_ContainNoPrivateKeyMaterial(string sample)
    {
        using var keys = CodeSamples.Keys(sample);
        var exported = new byte[KeySet.ExportLength];
        Assert.True(keys.TryExport(exported, out _));
        var bytes = CodeSamples.Create(sample).ToBytes();

        foreach (var secret in Secrets(exported))
        {
            for (var start = 0; start + 8 <= secret.Length; start++)
            {
                Assert.Equal(-1, bytes.AsSpan().IndexOf(secret.AsSpan(start, 8)));
            }
        }
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParse_CodeBuiltByHandFromTheSpecification_EqualsTheCodeOfCreate()
    {
        using var keys = TestKeys.Alice();

        var built = CodeBuilder.ForKeys(keys, "Alice");

        Assert.Equal(CodeSamples.Bytes(CodeSamples.Alice), built);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParse_Empty_IsDamaged()
    {
        Assert.False(ContactCode.TryParse([], out var code, out var error));

        Assert.Null(code);
        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void TryParse_OtherVersionWithAValidSignature_IsUnsupported(byte version)
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8("Alice"));

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.UnsupportedVersion, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(0)]
    [InlineData(65)]
    [InlineData(255)]
    public void TryParse_NameLengthOutsideTheLimits_IsDamaged(int length)
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            new byte[length],
            length);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(4)]
    [InlineData(6)]
    public void TryParse_NameLengthThatDisagreesWithTheCode_IsDamaged(int declared)
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8("Alice"),
            declared);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData("Alice ")]
    [InlineData(" Alice")]
    [InlineData("Al\nice")]
    [InlineData("Al\u202Eice")]
    [InlineData("Al\u200Bice")]
    public void TryParse_NameThatIsNotCanonicalButSignedByTheOwner_IsDamaged(string name)
    {
        using var keys = TestKeys.Alice();

        var bytes = CodeBuilder.ForKeys(keys, name);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));
        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(new byte[] { 0xC0, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xFF })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xC3 })]
    public void TryParse_NameOfInvalidUtf8SignedByTheOwner_IsDamaged(byte[] name)
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            name);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4eea")]
    [InlineData("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800")]
    [InlineData("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    public void TryParse_StaticKeyThatIsNotCanonicalOrHasSmallOrderSignedByTheOwner_IsDamaged(string staticKeyHex)
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            Convert.FromHexString(staticKeyHex),
            keys.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8("Alice"));

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(32)]
    [InlineData(33)]
    public void TryParse_OnionAddressWithAWrongChecksumSignedByTheOwner_IsDamaged(int checksumIndex)
    {
        using var keys = TestKeys.Alice();
        var address = keys.OnionAddress.Bytes.ToArray();
        address[checksumIndex] ^= 0x01;
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            address,
            CodeSamples.Utf8("Alice"));

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(255)]
    public void TryParse_OnionAddressOfAnotherVersionSignedByTheOwner_IsDamaged(byte version)
    {
        using var keys = TestKeys.Alice();
        var address = keys.OnionAddress.Bytes.ToArray();
        address[^1] = version;
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            address,
            CodeSamples.Utf8("Alice"));

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public void TryParse_IdentityKeyOfSomeoneElseWithTheSignatureOfTheCodesOwner_IsDamaged()
    {
        using var alice = TestKeys.Alice();
        using var bob = TestKeys.Bob();
        var bytes = CodeBuilder.Build(
            alice,
            ContactCode.Version,
            bob.IdentityKey.Bytes.ToArray(),
            alice.StaticKey.Bytes.ToArray(),
            alice.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8("Alice"));

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    [Trait("Story", "US-04")]
    public void TryParse_KeysOfOneCodeUnderTheSignatureOfAnother_AreDamaged()
    {
        var alice = CodeSamples.Bytes(CodeSamples.Alice);
        var bob = CodeSamples.Bytes(CodeSamples.Bob);

        foreach (var (offset, length) in new[] { (ContactCode.IdentityOffset, 32), (ContactCode.StaticOffset, 32), (ContactCode.OnionOffset, OnionAddress.Length) })
        {
            var mixed = (byte[])alice.Clone();
            bob.AsSpan(offset, length).CopyTo(mixed.AsSpan(offset, length));

            Assert.False(ContactCode.TryParse(mixed, out _, out var error), $"field at {offset}");
            Assert.Equal(ContactCodeError.Damaged, error);
        }
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParse_SignatureWithoutTheContext_IsDamaged()
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeBuilder.Build(
            keys,
            ContactCode.Version,
            keys.IdentityKey.Bytes.ToArray(),
            keys.StaticKey.Bytes.ToArray(),
            keys.OnionAddress.Bytes.ToArray(),
            CodeSamples.Utf8("Alice"),
            withContext: false);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParse_StructurallyPlausibleCodeWithoutASignature_IsDamaged()
    {
        var bytes = new byte[CodeSamples.Bytes(CodeSamples.Alice).Length];
        bytes[0] = ContactCode.Version;
        bytes[ContactCode.NameLengthOffset] = 5;
        Encoding.ASCII.GetBytes("Alice").CopyTo(bytes, ContactCode.HeaderLength);

        Assert.False(ContactCode.TryParse(bytes, out _, out var error));

        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void Signature_OfTheIdentityKey_IsOverTheContextAndTheBody()
    {
        using var keys = TestKeys.Alice();
        var bytes = CodeSamples.Bytes(CodeSamples.Alice);
        var body = bytes[..^ContactCode.SignatureLength];
        var signature = bytes[^ContactCode.SignatureLength..];

        Assert.True(keys.IdentityKey.Verify(ContactCode.SignedMessage(body), signature));
        Assert.False(keys.IdentityKey.Verify(body, signature));
        Assert.Equal("P2PM/contact-code/v1"u8.ToArray(), ContactCode.SignedMessage(body)[..20]);
    }

    private static IEnumerable<byte[]> Secrets(byte[] exported)
    {
        var identitySeed = exported[1..33];
        var staticPrivateKey = exported[33..65];
        var onionSeed = exported[65..97];

        yield return identitySeed;
        yield return staticPrivateKey;
        yield return onionSeed;
        yield return SHA512.HashData(identitySeed);
        yield return SHA512.HashData(onionSeed);
    }
}
