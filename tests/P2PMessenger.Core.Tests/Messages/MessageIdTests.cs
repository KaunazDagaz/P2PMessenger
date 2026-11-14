using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Messages;

public sealed class MessageIdTests
{
    [Fact]
    [Trait("Story", "US-05")]
    public void Create_ManyIds_AreSixteenRandomBytesAndAllDifferent()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => MessageId.Create()).ToList();

        Assert.All(ids, id => Assert.Equal(MessageId.Length, id.Bytes.Length));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.Count, ids.Select(id => Convert.ToHexStringLower(id.Bytes)).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void TryCreate_WrongLength_IsRefused(int length)
    {
        Assert.False(MessageId.TryCreate(new byte[length], out var id));

        Assert.Null(id);
    }

    [Fact]
    public void TryCreate_SameBytes_GivesEqualIds()
    {
        var bytes = Convert.FromHexString("00112233445566778899aabbccddeeff");

        Assert.True(MessageId.TryCreate(bytes, out var first));
        Assert.True(MessageId.TryCreate(bytes, out var second));
        bytes[0] = 0xFF;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("00112233445566778899aabbccddeeff", Convert.ToHexStringLower(first.Bytes));
    }
}
