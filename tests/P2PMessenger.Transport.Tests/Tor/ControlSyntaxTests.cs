using P2PMessenger.Transport.Tor;

namespace P2PMessenger.Transport.Tests.Tor;

public sealed class ControlSyntaxTests
{
    [Fact]
    public void TryParseArguments_WordsAndKeywords_SplitsThem()
    {
        Assert.True(ControlSyntax.TryParseArguments("STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=50 TAG=loading SUMMARY=\"Loading data\"", out var positional, out var keywords));

        Assert.Equal(["STATUS_CLIENT", "NOTICE", "BOOTSTRAP"], positional);
        Assert.Equal("50", keywords["PROGRESS"]);
        Assert.Equal("loading", keywords["TAG"]);
        Assert.Equal("Loading data", keywords["SUMMARY"]);
    }

    [Fact]
    public void TryParseArguments_QuotedEscapes_AreDecoded()
    {
        Assert.True(ControlSyntax.TryParseArguments("COOKIEFILE=\"C:\\\\Users\\\\a b\\\\cookie\" X=\"q\\\"t\\101\"", out _, out var keywords));

        Assert.Equal("C:\\Users\\a b\\cookie", keywords["COOKIEFILE"]);
        Assert.Equal("q\"tA", keywords["X"]);
    }

    [Fact]
    public void TryParseArguments_QuotedListOfListeners_GivesEachListener()
    {
        Assert.True(ControlSyntax.TryParseArguments("\"127.0.0.1:9050\" \"[::1]:9050\"", out var positional, out _));

        Assert.Equal(["127.0.0.1:9050", "[::1]:9050"], positional);
    }

    [Theory]
    [InlineData("X=\"open")]
    [InlineData("X=\"ends with a backslash\\")]
    [InlineData("\"open")]
    [InlineData("X=\"\\777\"")]
    public void TryParseArguments_BrokenQuotes_ReturnsFalse(string text)
    {
        Assert.False(ControlSyntax.TryParseArguments(text, out _, out _));
    }

    [Fact]
    public void TryParseArguments_TooManyArguments_ReturnsFalse()
    {
        var text = string.Join(' ', Enumerable.Range(0, 65).Select(static index => "a" + index));

        Assert.False(ControlSyntax.TryParseArguments(text, out _, out _));
    }

    [Fact]
    public void TryParseArguments_EmptyText_GivesNothing()
    {
        Assert.True(ControlSyntax.TryParseArguments("   ", out var positional, out var keywords));

        Assert.Empty(positional);
        Assert.Empty(keywords);
    }
}
