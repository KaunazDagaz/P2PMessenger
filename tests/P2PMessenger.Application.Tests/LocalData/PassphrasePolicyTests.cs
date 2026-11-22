using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Application.Tests.LocalData;

public sealed class PassphrasePolicyTests
{
    [Theory]
    [Trait("Story", "US-20")]
    [InlineData(null, PassphraseProblem.TooShort)]
    [InlineData("", PassphraseProblem.TooShort)]
    [InlineData("abcdefghijk", PassphraseProblem.TooShort)]
    [InlineData("abcdefghijkl", PassphraseProblem.None)]
    [InlineData("           .", PassphraseProblem.None)]
    [InlineData("1234 5678 90", PassphraseProblem.None)]
    [InlineData("ﬁﬁﬁﬁﬁﬁ", PassphraseProblem.None)]
    [InlineData("ﬁﬁﬁﬁﬁ", PassphraseProblem.TooShort)]
    [InlineData("🔑🔑🔑🔑🔑🔑", PassphraseProblem.TooShort)]
    [InlineData("пароль для вас", PassphraseProblem.None)]
    public void Check_CountsCharactersAndSetsNoOtherRule(string? passphrase, PassphraseProblem expected)
    {
        Assert.Equal(expected, PassphrasePolicy.Check(passphrase));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public void Check_LoneSurrogate_IsNotText()
    {
        Assert.Equal(PassphraseProblem.NotText, PassphrasePolicy.Check(new string(['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', '\ud800'])));
    }

    [Fact]
    public void MinimumLength_IsTwelve()
    {
        Assert.Equal(12, PassphrasePolicy.MinimumLength);
    }
}
