using System.Text;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Core.Tests.Identity;

public sealed class DisplayNameTests
{
    [Theory]
    [InlineData("Alice")]
    [InlineData("Mikita Besau")]
    [InlineData("Микита")]
    [InlineData("林 小明")]
    [InlineData("Nasrin\u200Cjan")]
    [InlineData("A\u200DB")]
    [InlineData("Ana 🙂")]
    [InlineData("a")]
    public void TryCreate_OrdinaryNames_AreAccepted(string text)
    {
        Assert.True(DisplayName.TryCreate(text, out var name));
        Assert.Equal(text, name.Value);
    }

    [Fact]
    public void TryCreate_SurroundingWhitespace_IsTrimmed()
    {
        Assert.True(DisplayName.TryCreate(" \t Alice \u00A0\n", out var name));
        Assert.Equal("Alice", name.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0\u2003")]
    public void TryCreate_NothingButWhitespace_IsRejected(string? text)
    {
        Assert.False(DisplayName.TryCreate(text, out _));
    }

    [Fact]
    public void TryCreate_LongestAndOneBeyond_AreSeparated()
    {
        Assert.True(DisplayName.TryCreate(new string('x', DisplayName.MaxUtf8Length), out _));
        Assert.False(DisplayName.TryCreate(new string('x', DisplayName.MaxUtf8Length + 1), out _));
        Assert.True(DisplayName.TryCreate(new string('я', DisplayName.MaxUtf8Length / 2), out _));
        Assert.False(DisplayName.TryCreate(new string('я', (DisplayName.MaxUtf8Length / 2) + 1), out _));
        Assert.False(DisplayName.TryCreate(new string('x', 100_000), out _));
    }

    [Theory]
    [InlineData("Al\u0000ice")]
    [InlineData("Al\nice")]
    [InlineData("Al\tice")]
    [InlineData("Al\u0085ice")]
    [InlineData("Al\u2028ice")]
    [InlineData("Al\u2029ice")]
    [InlineData("Al\u202Eice")]
    [InlineData("Al\u2066ice")]
    [InlineData("Al\u200Eice")]
    [InlineData("Al\u200Bice")]
    [InlineData("Al\uFEFFice")]
    [InlineData("Al\u00ADice")]
    [InlineData("Al\uE000ice")]
    [InlineData("Al\uFFFDice")]
    [InlineData("Al\uD800ice")]
    [InlineData("Alice\uDC00")]
    public void TryCreate_ControlFormatSeparatorPrivateAndBrokenCharacters_AreRejected(string text)
    {
        Assert.False(DisplayName.TryCreate(text, out _));
    }

    [Theory]
    [InlineData("Alice")]
    [InlineData("Микита")]
    [InlineData("Ana 🙂")]
    public void TryParse_EncodedName_GivesTheSameName(string text)
    {
        Assert.True(DisplayName.TryCreate(text, out var created));

        Assert.True(DisplayName.TryParse(created.Utf8Bytes, out var parsed));
        Assert.Equal(created, parsed);
        Assert.Equal(Encoding.UTF8.GetBytes(text), created.Utf8Bytes.ToArray());
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x20 })]
    [InlineData(new byte[] { 0x41, 0x20 })]
    [InlineData(new byte[] { 0x20, 0x41 })]
    [InlineData(new byte[] { 0x41, 0x0A })]
    [InlineData(new byte[] { 0xC0, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xC3 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xFF })]
    [InlineData(new byte[] { 0xE2, 0x80, 0xAE })]
    public void TryParse_NonCanonicalOrInvalidBytes_AreRejected(byte[] bytes)
    {
        Assert.False(DisplayName.TryParse(bytes, out _));
    }

    [Fact]
    public void TryParse_MoreThanTheLimit_IsRejected()
    {
        Assert.True(DisplayName.TryParse(Encoding.UTF8.GetBytes(new string('x', 64)), out _));
        Assert.False(DisplayName.TryParse(Encoding.UTF8.GetBytes(new string('x', 65)), out _));
    }

    [Fact]
    public void Equals_SameText_IsTrue()
    {
        Assert.True(DisplayName.TryCreate("Alice", out var first));
        Assert.True(DisplayName.TryCreate("Alice", out var second));
        Assert.True(DisplayName.TryCreate("alice", out var third));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, third);
    }
}
