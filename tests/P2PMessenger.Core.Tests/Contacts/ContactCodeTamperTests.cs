using P2PMessenger.Core.Contacts;

namespace P2PMessenger.Core.Tests.Contacts;

public sealed class ContactCodeTamperTests
{
    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParse_AnySingleBitFlipOfTheCode_IsRejected(string sample)
    {
        var bytes = CodeSamples.Bytes(sample);

        for (var bit = 0; bit < bytes.Length * 8; bit++)
        {
            var changed = (byte[])bytes.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.False(ContactCode.TryParse(changed, out var code, out _), $"bit {bit} of {bytes.Length * 8}");
            Assert.Null(code);
        }
    }

    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParse_EveryTruncationOfTheCode_IsRejected(string sample)
    {
        var bytes = CodeSamples.Bytes(sample);

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.False(ContactCode.TryParse(bytes.AsSpan(0, length), out _, out _), $"length {length}");
        }
    }

    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParse_AnythingAppendedToTheCode_IsRejected(string sample)
    {
        var bytes = CodeSamples.Bytes(sample);

        foreach (var extra in new byte[][] { [0], [255], new byte[ContactCode.SignatureLength], new byte[300] })
        {
            Assert.False(ContactCode.TryParse([.. bytes, .. extra], out _, out _), $"{extra.Length} bytes appended");
        }
    }

    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParseText_AnySingleBitFlipOfAnyCharacter_IsRejectedOrChangesNothing(string sample)
    {
        var original = CodeSamples.Parse(sample);
        var text = CodeSamples.Text(sample);

        for (var index = 0; index < text.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var changed = text.ToCharArray();
                changed[index] = (char)(changed[index] ^ (1 << bit));

                if (ContactCode.TryParseText(new string(changed), out var code, out _))
                {
                    Assert.Equal(original, code);
                }
            }
        }
    }

    [Fact]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    public void TryParseText_AnyOtherCharacterAtAnyPositionOfTheBody_IsRejected()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var text = CodeSamples.Text(CodeSamples.Alice);

        for (var index = ContactCode.TextPrefix.Length; index < text.Length; index++)
        {
            foreach (var replacement in alphabet.Where(candidate => candidate != text[index]))
            {
                var changed = text.ToCharArray();
                changed[index] = replacement;

                Assert.False(ContactCode.TryParseText(new string(changed), out _, out _), $"position {index} replaced by {replacement}");
            }
        }
    }

    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParseText_EveryTruncationOfTheText_IsRejected(string sample)
    {
        var text = CodeSamples.Text(sample);

        for (var length = 0; length < text.Length; length++)
        {
            Assert.False(ContactCode.TryParseText(text[..length], out _, out _), $"length {length}");
        }
    }

    [Theory]
    [Trait("Story", "US-02")]
    [Trait("NFR", "NFR-09")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParseText_AnyCharacterAppendedToTheText_IsRejected(string sample)
    {
        var text = CodeSamples.Text(sample);

        foreach (var extra in "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567019=!")
        {
            Assert.False(ContactCode.TryParseText(text + extra, out _, out _), $"appended {extra}");
        }
    }
}
