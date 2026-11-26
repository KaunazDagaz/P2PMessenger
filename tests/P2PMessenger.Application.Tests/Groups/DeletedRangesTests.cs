using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Tests.Groups;

public sealed class DeletedRangesTests
{
    [Fact]
    [Trait("Story", "US-21")]
    public void With_SequencesNextToEachOtherOrInsideARange_AreMergedPerAuthor()
    {
        using var bob = KeySet.Create();
        using var carol = KeySet.Create();

        var ranges = DeletedRanges.Empty.With([(bob.IdentityKey, 3), (bob.IdentityKey, 1), (bob.IdentityKey, 2), (bob.IdentityKey, 7), (carol.IdentityKey, 5)]);
        ranges = ranges.With([(bob.IdentityKey, 2), (bob.IdentityKey, 6)]);

        var list = ranges.ToList();
        Assert.Equal(3, list.Count);
        Assert.Contains(new AuthorSequenceRange(bob.IdentityKey, 1, 3), list);
        Assert.Contains(new AuthorSequenceRange(bob.IdentityKey, 6, 7), list);
        Assert.Contains(new AuthorSequenceRange(carol.IdentityKey, 5, 5), list);
        Assert.True(ranges.Contains(bob.IdentityKey, 2));
        Assert.False(ranges.Contains(bob.IdentityKey, 4));
        Assert.False(ranges.Contains(carol.IdentityKey, 4));
        Assert.Equal(7, ranges.HighestOf(bob.IdentityKey));
        Assert.Equal(5, ranges.HighestOf(carol.IdentityKey));
        Assert.False(ranges.IsEmpty);
    }

    [Fact]
    [Trait("Story", "US-21")]
    public void ToList_IsSortedByAuthorKeyBytesThenFirstSequence_AndFromGivesTheSameRanges()
    {
        using var first = KeySet.Create();
        using var second = KeySet.Create();
        var ranges = DeletedRanges.Empty.With([(first.IdentityKey, 9), (second.IdentityKey, 4), (first.IdentityKey, 2), (second.IdentityKey, 1)]);

        var list = ranges.ToList();

        var expected = list.OrderBy(static range => Convert.ToHexString(range.AuthorKey.Bytes), StringComparer.Ordinal).ThenBy(static range => range.First).ToList();
        Assert.Equal(expected, list);
        Assert.Equal(4, list.Count);
        Assert.Equal(list, DeletedRanges.From(list).ToList());
    }

    [Fact]
    [Trait("Story", "US-21")]
    public void Empty_HasNothingAndTheHighestIsZero()
    {
        using var bob = KeySet.Create();

        Assert.True(DeletedRanges.Empty.IsEmpty);
        Assert.Empty(DeletedRanges.Empty.ToList());
        Assert.False(DeletedRanges.Empty.Contains(bob.IdentityKey, 1));
        Assert.Equal(0, DeletedRanges.Empty.HighestOf(bob.IdentityKey));
        Assert.True(DeletedRanges.From([]).IsEmpty);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 3)]
    [Trait("Story", "US-21")]
    public void From_RangeThatDoesNotStartAtOneOrEndsBeforeItStarts_Throws(long first, long last)
    {
        using var bob = KeySet.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => DeletedRanges.From([new AuthorSequenceRange(bob.IdentityKey, first, last)]));
    }
}
