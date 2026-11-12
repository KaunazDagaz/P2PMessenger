using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Text;

namespace P2PMessenger.Core.Tests.Contacts;

public sealed class ContactCodeTextTests
{
    [Theory]
    [Trait("Story", "US-02")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParseText_ReferenceText_GivesTheCode(string sample)
    {
        Assert.True(ContactCode.TryParseText(CodeSamples.Text(sample), out var code, out var error));

        Assert.Equal(ContactCodeError.None, error);
        Assert.Equal(CodeSamples.Parse(sample), code);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void ToText_Code_IsOneLineOfPrefixAndUppercaseBase32()
    {
        var text = CodeSamples.Create(CodeSamples.Bob).ToText();

        Assert.StartsWith(ContactCode.TextPrefix, text, StringComparison.Ordinal);
        Assert.All(text[ContactCode.TextPrefix.Length..], c => Assert.True(c is (>= 'A' and <= 'Z') or (>= '2' and <= '7'), $"unexpected {c}"));
    }

    [Theory]
    [Trait("Story", "US-02")]
    [MemberData(nameof(CodeSamples.Names), MemberType = typeof(CodeSamples))]
    public void TryParseText_TextChangedByOtherMessengers_GivesTheSameCode(string sample)
    {
        var expected = CodeSamples.Parse(sample);
        var text = CodeSamples.Text(sample);

        foreach (var mangled in Mangled(text))
        {
            Assert.True(ContactCode.TryParseText(mangled, out var code, out _), mangled);
            Assert.Equal(expected, code);
        }
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_SpanOfTheText_GivesTheCode()
    {
        Assert.True(ContactCode.TryParseText(CodeSamples.Text(CodeSamples.Alice).AsSpan(), out var code, out _));

        Assert.Equal(CodeSamples.Parse(CodeSamples.Alice), code);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_PrefixOfAnotherVersion_IsUnsupported()
    {
        var text = "P2PM2-" + CodeSamples.Text(CodeSamples.Alice)[ContactCode.TextPrefix.Length..];

        Assert.False(ContactCode.TryParseText(text, out var code, out var error));

        Assert.Null(code);
        Assert.Equal(ContactCodeError.UnsupportedVersion, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_BodyOfAnotherVersionUnderTheFirstPrefix_IsUnsupported()
    {
        var bytes = CodeSamples.Bytes(CodeSamples.Alice);
        bytes[0] = 2;
        var text = ContactCode.TextPrefix + Base32.Encode(bytes, lowerCase: false);

        Assert.False(ContactCode.TryParseText(text, out _, out var error));

        Assert.Equal(ContactCodeError.UnsupportedVersion, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("P2PM")]
    [InlineData("P2PX1-ABCDEFGH")]
    [InlineData("https://example.com/p2pm1-abcdefgh")]
    [InlineData("WIFI:T:WPA;S:network;P:password;;")]
    [InlineData("BEGIN:VCARD\nVERSION:3.0\nFN:Alice\nEND:VCARD")]
    [InlineData("{\"p2pm\":1}")]
    [InlineData("-----BEGIN PGP PUBLIC KEY BLOCK-----")]
    [InlineData("mailto:alice@example.com")]
    public void TryParseText_ContentOfAnotherAppOrKind_IsNotAContactCode(string? text)
    {
        Assert.False(ContactCode.TryParseText(text, out var code, out var error));

        Assert.Null(code);
        Assert.Equal(ContactCodeError.NotAContactCode, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_CodeInsideAnotherText_IsNotAContactCode()
    {
        var text = "Here is my code: " + CodeSamples.Text(CodeSamples.Alice) + " Please add me.";

        Assert.False(ContactCode.TryParseText(text, out _, out var error));

        Assert.Equal(ContactCodeError.NotAContactCode, error);
    }

    [Theory]
    [Trait("Story", "US-02")]
    [InlineData("P2PM1")]
    [InlineData("P2PM1-")]
    [InlineData("P2PM1-A")]
    [InlineData("P2PM1-AEAAAAAA")]
    [InlineData("P2PM1-0123456789")]
    public void TryParseText_OurPrefixWithABodyThatIsNoCode_IsDamaged(string text)
    {
        Assert.False(ContactCode.TryParseText(text, out var code, out var error));

        Assert.Null(code);
        Assert.Equal(ContactCodeError.Damaged, error);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_VeryLongInput_IsRejectedWithoutReadingItAll()
    {
        var padded = CodeSamples.Text(CodeSamples.Alice) + new string(' ', 5000);
        var endless = ContactCode.TextPrefix + new string('A', 100_000);

        Assert.False(ContactCode.TryParseText(padded, out _, out var paddedError));
        Assert.False(ContactCode.TryParseText(endless, out _, out var endlessError));
        Assert.Equal(ContactCodeError.NotAContactCode, paddedError);
        Assert.Equal(ContactCodeError.NotAContactCode, endlessError);
    }

    [Fact]
    [Trait("Story", "US-02")]
    public void TryParseText_LongestCodeWithManySeparators_IsStillAccepted()
    {
        var text = CodeSamples.Text(CodeSamples.Longest);
        var grouped = string.Join("-\r\n ", text.Chunk(5).Select(chunk => new string(chunk)));

        Assert.True(grouped.Length < 4096);
        Assert.True(ContactCode.TryParseText(grouped, out var code, out _));
        Assert.Equal(CodeSamples.Parse(CodeSamples.Longest), code);
    }

    private static IEnumerable<string> Mangled(string text)
    {
        yield return text.ToLowerInvariant();
        yield return string.Concat(text.Select((c, i) => i % 2 == 0 ? char.ToLowerInvariant(c) : c));
        yield return " " + text + "\r\n";
        yield return "\uFEFF" + text;
        yield return string.Join("\r\n", text.Chunk(40).Select(chunk => new string(chunk)));
        yield return string.Join(' ', text.Chunk(5).Select(chunk => new string(chunk)));
        yield return string.Join('-', text.Chunk(8).Select(chunk => new string(chunk)));
        yield return string.Join('\t', text.Chunk(11).Select(chunk => new string(chunk)));
        yield return text.Replace("-", "\u2011", StringComparison.Ordinal);
        yield return text.Replace("-", "\u2013", StringComparison.Ordinal);
        yield return text.Replace("-", " \u00A0", StringComparison.Ordinal);
        yield return string.Concat(text.Select((c, i) => i % 7 == 3 ? c + "\u200B" : c.ToString()));
        yield return text.Replace("-", string.Empty, StringComparison.Ordinal);
        yield return text.Replace("P2PM1-", "p2pm1 ", StringComparison.Ordinal);
    }
}
