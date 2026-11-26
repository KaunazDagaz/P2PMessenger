using System.Buffers.Binary;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Tests.Groups;

public sealed class GroupSummaryTests
{
    [Fact]
    [Trait("Story", "US-13")]
    public void Encode_EmptySummary_GivesTheContentOfAnIndependentImplementation()
    {
        var summary = GroupSummary.Create(GroupSamples.VectorGroupId(), 1, []);
        var plaintext = MessageCodec.Encode(summary);

        Assert.Equal(MessageCodec.MinPaddedLength, plaintext.Length);
        Assert.Equal(GroupVectors.EmptySummaryContent, Content(plaintext));
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Encode_SummaryWithAuthorsAndRanges_GivesTheContentOfAnIndependentImplementation()
    {
        var alice = GroupSamples.Key(TestKeys.Rfc8032Public1);
        var carol = GroupSamples.Key(TestKeys.Rfc8032Public2);
        var summary = GroupSummary.Create(
            GroupSamples.VectorGroupId(),
            1,
            [
                new GroupSummaryAuthor(alice, [new SequenceRange(1, 3), new SequenceRange(5, 5)]),
                new GroupSummaryAuthor(carol, [new SequenceRange(2, 4)]),
            ]);
        var plaintext = MessageCodec.Encode(summary);

        Assert.Equal(MessageCodec.MinPaddedLength, plaintext.Length);
        Assert.Equal(GroupVectors.SummaryContent, Content(plaintext));
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Decode_VectorSummary_GivesItsAuthorsRangesAndContainsMatches()
    {
        var alice = GroupSamples.Key(TestKeys.Rfc8032Public1);
        var carol = GroupSamples.Key(TestKeys.Rfc8032Public2);
        var bob = GroupSamples.Key(TestKeys.Rfc8032Public3);

        Assert.True(MessageCodec.TryDecode(Pad(Convert.FromHexString(GroupVectors.SummaryContent)), out var decoded));
        var summary = Assert.IsType<GroupSummary>(decoded);

        Assert.Equal(GroupSamples.VectorGroupId(), summary.GroupId);
        Assert.Equal(1, summary.Epoch);
        Assert.Equal([carol, alice], summary.Authors.Select(static author => author.AuthorKey));
        Assert.Equal([new SequenceRange(2, 4)], summary.Authors[0].Ranges);
        Assert.Equal([new SequenceRange(1, 3), new SequenceRange(5, 5)], summary.Authors[1].Ranges);
        Assert.True(summary.Contains(alice, 1));
        Assert.True(summary.Contains(alice, 3));
        Assert.False(summary.Contains(alice, 4));
        Assert.True(summary.Contains(alice, 5));
        Assert.False(summary.Contains(alice, 6));
        Assert.False(summary.Contains(carol, 1));
        Assert.True(summary.Contains(carol, 2));
        Assert.True(summary.Contains(carol, 4));
        Assert.False(summary.Contains(carol, 5));
        Assert.False(summary.Contains(bob, 1));
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Fit_UnsortedTouchingAndOverlappingRanges_MergesAndMatchesVector()
    {
        var alice = GroupSamples.Key(TestKeys.Rfc8032Public1);
        var carol = GroupSamples.Key(TestKeys.Rfc8032Public2);
        var bob = GroupSamples.Key(TestKeys.Rfc8032Public3);

        var summary = GroupSummary.Fit(
            GroupSamples.VectorGroupId(),
            1,
            [
                (alice, [new SequenceRange(5, 5), new SequenceRange(2, 3), new SequenceRange(1, 2)]),
                (bob, []),
                (carol, [new SequenceRange(4, 4), new SequenceRange(2, 3)]),
            ]);

        Assert.Equal(GroupVectors.SummaryContent, Content(MessageCodec.Encode(summary)));
        Assert.Equal(summary, GroupSummary.Create(GroupSamples.VectorGroupId(), 1, summary.Authors));
        Assert.Equal(summary.GetHashCode(), GroupSummary.Create(GroupSamples.VectorGroupId(), 1, summary.Authors).GetHashCode());
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Fit_Full160MembersWithOneRangeEach_FitsOneFrameAndRoundTrips()
    {
        var held = new List<(Ed25519PublicKey AuthorKey, IEnumerable<SequenceRange> Ranges)>(GroupSummary.MaxAuthors);
        for (var index = 0; index < GroupSummary.MaxAuthors; index++)
        {
            using var keySet = KeySet.Create();
            held.Add((keySet.IdentityKey, [new SequenceRange(1, index + 1)]));
        }

        var summary = GroupSummary.Fit(GroupId.Create(), 1, held);
        var plaintext = MessageCodec.Encode(summary);

        Assert.Equal(GroupSummary.MaxAuthors, summary.Authors.Count);
        Assert.Equal(7_861, summary.BodyLength);
        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        Assert.Equal(summary, decoded);
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Fit_OneAuthorWithMoreThan255DisjointRanges_KeepsTheNewest255WithoutMergingGaps()
    {
        var alice = GroupSamples.Key(TestKeys.Rfc8032Public1);
        var disjoint = Enumerable.Range(0, 300).Select(static index => new SequenceRange((index * 2) + 1, (index * 2) + 1)).ToArray();

        var summary = GroupSummary.Fit(GroupId.Create(), 1, [(alice, disjoint)]);
        var plaintext = MessageCodec.Encode(summary);

        var author = Assert.Single(summary.Authors);
        Assert.Equal(GroupSummary.MaxRangesPerAuthor, author.Ranges.Count);
        Assert.Equal(new SequenceRange((45 * 2) + 1, (45 * 2) + 1), author.Ranges[0]);
        Assert.Equal(new SequenceRange((299 * 2) + 1, (299 * 2) + 1), author.Ranges[^1]);
        Assert.False(summary.Contains(alice, 1));
        Assert.False(summary.Contains(alice, (45 * 2) + 2));
        Assert.True(summary.Contains(alice, (299 * 2) + 1));
        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        Assert.Equal(summary, decoded);
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Fit_160AuthorsWithTenRangesEach_KeepsNewestRangesRoundRobinWithinOneFrame()
    {
        var held = new List<(Ed25519PublicKey AuthorKey, IEnumerable<SequenceRange> Ranges)>(GroupSummary.MaxAuthors);
        for (var index = 0; index < GroupSummary.MaxAuthors; index++)
        {
            using var keySet = KeySet.Create();
            var ranges = Enumerable.Range(0, 10).Select(static gap => new SequenceRange((gap * 2) + 1, (gap * 2) + 1)).ToArray();
            held.Add((keySet.IdentityKey, ranges));
        }

        var summary = GroupSummary.Fit(GroupId.Create(), 1, held);
        var plaintext = MessageCodec.Encode(summary);

        Assert.Equal(GroupSummary.MaxAuthors, summary.Authors.Count);
        Assert.InRange(summary.BodyLength, GroupSummary.MaxBodyLength - GroupSummary.RangeLength + 1, GroupSummary.MaxBodyLength);
        Assert.All(summary.Authors, static author =>
        {
            Assert.InRange(author.Ranges.Count, 4, 5);
            Assert.Equal(new SequenceRange(19, 19), author.Ranges[^1]);
            Assert.False(author.Contains(18));
            Assert.False(author.Contains(1));
        });
        Assert.Equal(MessageCodec.MaxPaddedLength, plaintext.Length);
        Assert.True(MessageCodec.TryDecode(plaintext, out var decoded));
        Assert.Equal(summary, decoded);
    }

    [Fact]
    [Trait("Story", "US-13")]
    public void Create_AndConstructors_InvalidInputs_Throw()
    {
        var alice = GroupSamples.Key(TestKeys.Rfc8032Public1);
        var validAuthor = new GroupSummaryAuthor(alice, [new SequenceRange(1, 2)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceRange(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceRange(3, 2));
        Assert.Throws<ArgumentException>(() => new GroupSummaryAuthor(alice, []));
        Assert.Throws<ArgumentException>(() => new GroupSummaryAuthor(alice, [default]));
        Assert.Throws<ArgumentException>(() => new GroupSummaryAuthor(alice, [new SequenceRange(1, 2), new SequenceRange(3, 4)]));
        Assert.Throws<ArgumentException>(() => new GroupSummaryAuthor(alice, [new SequenceRange(2, 4), new SequenceRange(3, 5)]));
        Assert.Throws<ArgumentException>(() => new GroupSummaryAuthor(alice, [new SequenceRange(1, long.MaxValue), new SequenceRange(long.MaxValue, long.MaxValue)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupSummary.Create(GroupId.Create(), 0, []));
        Assert.Throws<ArgumentException>(() => GroupSummary.Create(GroupId.Create(), 1, [validAuthor, validAuthor]));
        Assert.Throws<ArgumentException>(() => GroupSummary.Create(GroupId.Create(), 1, [null!]));

        var tooMany = new List<GroupSummaryAuthor>();
        for (var index = 0; index <= GroupSummary.MaxAuthors; index++)
        {
            using var keySet = KeySet.Create();
            tooMany.Add(new GroupSummaryAuthor(keySet.IdentityKey, [new SequenceRange(1, 1)]));
        }

        Assert.Throws<ArgumentException>(() => GroupSummary.Create(GroupId.Create(), 1, tooMany));
        Assert.Throws<ArgumentException>(() => GroupSummary.Fit(GroupId.Create(), 1, tooMany.Select(static author => (author.AuthorKey, (IEnumerable<SequenceRange>)author.Ranges))));

        var oversized = new List<GroupSummaryAuthor>();
        var manyRanges = Enumerable.Range(0, 200).Select(static index => new SequenceRange((index * 2) + 1, (index * 2) + 1)).ToArray();
        for (var index = 0; index < 6; index++)
        {
            using var keySet = KeySet.Create();
            oversized.Add(new GroupSummaryAuthor(keySet.IdentityKey, manyRanges));
        }

        Assert.Throws<ArgumentException>(() => GroupSummary.Create(GroupId.Create(), 1, oversized));
    }

    [Theory]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    [InlineData(GroupVectors.EmptySummaryContent)]
    [InlineData(GroupVectors.SummaryContent)]
    public void Decode_AnySingleBitFlip_IsRefusedOrDecodesToExactlyTheChangedBytes(string contentHex)
    {
        var plaintext = Pad(Convert.FromHexString(contentHex));

        for (var bit = 0; bit < plaintext.Length * 8; bit++)
        {
            var changed = (byte[])plaintext.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            if (MessageCodec.TryDecode(changed, out var decoded))
            {
                Assert.Equal(Convert.ToHexStringLower(changed), Convert.ToHexStringLower(MessageCodec.Encode(decoded)));
            }
        }
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public void Decode_EveryTruncationAndExtension_IsRefused()
    {
        var body = Convert.FromHexString(GroupVectors.SummaryContent)[2..];

        for (var length = 0; length < body.Length; length++)
        {
            byte[] content = [(byte)SessionMessageType.GroupSummary, MessageCodec.CurrentVersion, .. body[..length]];
            Assert.False(MessageCodec.TryDecode(Pad(content), out _), $"body length {length}");
        }

        foreach (var extra in new byte[][] { [0], [1], new byte[16] })
        {
            byte[] content = [(byte)SessionMessageType.GroupSummary, MessageCodec.CurrentVersion, .. body, .. extra];
            Assert.False(MessageCodec.TryDecode(Pad(content), out _), $"{extra.Length} bytes appended");
        }
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("NFR", "NFR-09")]
    public void Decode_InvalidFieldsAndOrdering_AreRefused()
    {
        var gid = Convert.FromHexString(GroupVectors.GroupId);
        var alice = Convert.FromHexString(TestKeys.Rfc8032Public1);
        var carol = Convert.FromHexString(TestKeys.Rfc8032Public2);

        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 0, 0]));
        Assert.False(TryDecodeBody([.. gid, 0x80, 0, 0, 0, 0]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 161]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 0]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 1, .. Range(0, 1)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 1, .. Range(3, 2)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 2, .. Range(1, 2), .. Range(3, 4)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 2, .. Range(1, 3), .. Range(3, 5)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 1, .. alice, 2, .. Range(1, long.MaxValue), .. Range(long.MaxValue, long.MaxValue)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 2, .. alice, 1, .. Range(1, 1), .. carol, 1, .. Range(1, 1)]));
        Assert.False(TryDecodeBody([.. gid, 0, 0, 0, 1, 2, .. carol, 1, .. Range(1, 1), .. carol, 1, .. Range(2, 2)]));
    }

    [Fact]
    [Trait("Story", "US-13")]
    [Trait("Story", "US-21")]
    public void SequenceRangeAndAuthorSequenceRange_ValidateMergeAndCover()
    {
        using var alice = KeySet.Create();

        Assert.False(SequenceRange.TryCreate(0, 1, out _));
        Assert.False(SequenceRange.TryCreate(3, 2, out _));
        Assert.True(SequenceRange.TryCreate(2, 5, out var range));
        Assert.True(range.Covers(new SequenceRange(2, 5)));
        Assert.True(range.Covers(new SequenceRange(3, 4)));
        Assert.False(range.Covers(new SequenceRange(1, 5)));
        Assert.False(range.Covers(new SequenceRange(2, 6)));

        var merged = SequenceRange.Merge([new SequenceRange(5, 6), new SequenceRange(1, 2), new SequenceRange(3, 4), new SequenceRange(8, 8)]);
        Assert.Equal([new SequenceRange(1, 6), new SequenceRange(8, 8)], merged);

        var authored = new AuthorSequenceRange(alice.IdentityKey, 2, 5);
        Assert.Equal(alice.IdentityKey, authored.AuthorKey);
        Assert.Equal(range, authored.Range);
        Assert.Equal(2, authored.First);
        Assert.Equal(5, authored.Last);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthorSequenceRange(alice.IdentityKey, default));
    }

    private static bool TryDecodeBody(byte[] body)
    {
        byte[] content = [(byte)SessionMessageType.GroupSummary, MessageCodec.CurrentVersion, .. body];
        return MessageCodec.TryDecode(Pad(content), out _);
    }

    private static byte[] Range(long first, long last)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(0, 8), first);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(8, 8), last);
        return bytes;
    }

    private static string Content(byte[] plaintext)
    {
        var length = (plaintext[0] << 8) | plaintext[1];
        return Convert.ToHexStringLower(plaintext.AsSpan(2, length));
    }

    private static byte[] Pad(byte[] content)
    {
        var plaintext = new byte[MessageCodec.PaddedLengthFor(content.Length)];
        plaintext[0] = (byte)(content.Length >> 8);
        plaintext[1] = (byte)content.Length;
        content.CopyTo(plaintext, 2);
        return plaintext;
    }
}
