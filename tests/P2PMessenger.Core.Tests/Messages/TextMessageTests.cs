using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Messages;

public sealed class TextMessageTests
{
    private static readonly DateTimeOffset SomeTime = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [Trait("Story", "US-11")]
    [InlineData("a")]
    [InlineData("Hello, Bob")]
    [InlineData("Микита")]
    [InlineData("line one\nline two\ttabbed")]
    [InlineData("\U0001F44B‍")]
    public void TryCreate_WellFormedText_IsAccepted(string text)
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, text, out var message));

        Assert.Equal(text, message.Text);
        Assert.Equal(1, message.Sequence);
        Assert.Equal(SomeTime, message.WrittenAt);
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void TryCreate_LongestText_IsAcceptedAndOneByteMoreIsNot()
    {
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, new string('x', TextMessage.MaxUtf8Length), out _));
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, new string('x', TextMessage.MaxUtf8Length + 1), out _));
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, new string('é', (TextMessage.MaxUtf8Length / 2) + 1), out _));
    }

    [Theory]
    [Trait("Story", "US-11")]
    [InlineData(null)]
    [InlineData("")]
    public void TryCreate_MissingText_IsRefused(string? text)
    {
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, text, out var message));

        Assert.Null(message);
    }

    [Theory]
    [Trait("Story", "US-11")]
    [InlineData(0xD800)]
    [InlineData(0xDBFF)]
    [InlineData(0xDC00)]
    [InlineData(0xDFFF)]
    public void TryCreate_TextWithALoneSurrogate_IsRefused(int surrogate)
    {
        var lone = new string((char)surrogate, 1);

        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, lone, out _));
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, "text " + lone + " more", out _));
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, SomeTime, "text " + lone, out _));
    }

    [Theory]
    [Trait("Story", "US-11")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void TryCreate_SequenceBelowOne_IsRefused(long sequence)
    {
        Assert.False(TextMessage.TryCreate(MessageId.Create(), sequence, SomeTime, "text", out _));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void TryCreate_TimeBefore1970_IsRefusedAndTheEpochIsNot()
    {
        Assert.False(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch.AddMilliseconds(-1), "text", out _));
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.UnixEpoch, "text", out _));
        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, DateTimeOffset.MaxValue, "text", out _));
    }

    [Fact]
    [Trait("Story", "US-11")]
    public void WrittenAt_WithMoreThanMillisecondPrecision_KeepsWholeMilliseconds()
    {
        var precise = SomeTime.AddTicks(12_345);

        Assert.True(TextMessage.TryCreate(MessageId.Create(), 1, precise, "text", out var message));

        Assert.Equal(SomeTime.AddMilliseconds(1), message.WrittenAt);
    }

    [Fact]
    public void Equals_SameFields_AreEqualAndAnyDifferentFieldIsNot()
    {
        var id = MessageId.Create();
        var message = Create(id, 3, SomeTime, "text");

        Assert.Equal(message, Create(id, 3, SomeTime, "text"));
        Assert.Equal(message.GetHashCode(), Create(id, 3, SomeTime, "text").GetHashCode());
        Assert.NotEqual(message, Create(MessageId.Create(), 3, SomeTime, "text"));
        Assert.NotEqual(message, Create(id, 4, SomeTime, "text"));
        Assert.NotEqual(message, Create(id, 3, SomeTime.AddSeconds(1), "text"));
        Assert.NotEqual(message, Create(id, 3, SomeTime, "Text"));
    }

    private static TextMessage Create(MessageId id, long sequence, DateTimeOffset writtenAt, string text)
    {
        if (!TextMessage.TryCreate(id, sequence, writtenAt, text, out var message))
        {
            throw new InvalidOperationException("The test message was not accepted.");
        }

        return message;
    }
}
