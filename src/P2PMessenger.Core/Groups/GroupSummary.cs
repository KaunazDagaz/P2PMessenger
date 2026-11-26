using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Core.Groups;

public sealed class GroupSummary : SessionMessage, IEquatable<GroupSummary>
{
    public const int MaxBodyLength = MessageCodec.MaxContentLength - MessageCodec.TypeHeaderLength;
    public const int MaxAuthors = GroupRecord.MaxMembers;
    public const int MaxRangesPerAuthor = byte.MaxValue;

    internal const int HeaderLength = GroupId.Length + sizeof(uint) + 1;
    internal const int AuthorHeaderLength = Ed25519PublicKey.Length + 1;
    internal const int RangeLength = 2 * sizeof(long);

    private readonly GroupSummaryAuthor[] authors;
    private readonly int bodyLength;

    private GroupSummary(GroupId groupId, int epoch, GroupSummaryAuthor[] authors, int bodyLength)
    {
        GroupId = groupId;
        Epoch = epoch;
        this.authors = authors;
        this.bodyLength = bodyLength;
    }

    public GroupId GroupId { get; }

    public int Epoch { get; }

    public IReadOnlyList<GroupSummaryAuthor> Authors => authors;

    public override SessionMessageType Type => SessionMessageType.GroupSummary;

    internal override int BodyLength => bodyLength;

    public static GroupSummary Create(GroupId groupId, int epoch, IEnumerable<GroupSummaryAuthor> authors)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(authors);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);

        List<GroupSummaryAuthor> sorted = [.. authors];
        if (sorted.Exists(static author => author is null))
        {
            throw new ArgumentException("An author entry cannot be null.", nameof(authors));
        }

        if (sorted.Count > MaxAuthors)
        {
            throw new ArgumentException($"A group summary carries at most {MaxAuthors} authors.", nameof(authors));
        }

        sorted.Sort(static (left, right) => GroupRecord.Compare(left.AuthorKey, right.AuthorKey));
        int length = HeaderLength;
        for (int index = 0; index < sorted.Count; index++)
        {
            if (index > 0 && sorted[index - 1].AuthorKey.Equals(sorted[index].AuthorKey))
            {
                throw new ArgumentException("An author can appear in a group summary only once.", nameof(authors));
            }

            length += AuthorHeaderLength + (sorted[index].Ranges.Count * RangeLength);
            if (length > MaxBodyLength)
            {
                throw new ArgumentException("The group summary does not fit into one frame.", nameof(authors));
            }
        }

        return new GroupSummary(groupId, epoch, [.. sorted], length);
    }

    public static GroupSummary Fit(GroupId groupId, int epoch, IEnumerable<(Ed25519PublicKey AuthorKey, IEnumerable<SequenceRange> Ranges)> held)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);

        Dictionary<Ed25519PublicKey, List<SequenceRange>> collected = [];
        foreach ((Ed25519PublicKey authorKey, IEnumerable<SequenceRange> ranges) in held)
        {
            ArgumentNullException.ThrowIfNull(authorKey);
            ArgumentNullException.ThrowIfNull(ranges);

            foreach (SequenceRange range in ranges)
            {
                if (range.First < 1 || range.Last < range.First)
                {
                    throw new ArgumentException("A sequence range starts at 1 or later and ends at or after its start.", nameof(held));
                }

                if (!collected.TryGetValue(authorKey, out List<SequenceRange>? list))
                {
                    list = [];
                    collected.Add(authorKey, list);
                }

                list.Add(range);
            }
        }

        if (collected.Count > MaxAuthors)
        {
            throw new ArgumentException($"A group summary carries at most {MaxAuthors} authors.", nameof(held));
        }

        if (collected.Count == 0)
        {
            return new GroupSummary(groupId, epoch, [], HeaderLength);
        }

        List<(Ed25519PublicKey Key, List<SequenceRange> Ranges)> normalized = [.. collected
            .OrderBy(static pair => pair.Key, AuthorOrder.Instance)
            .Select(static pair => (pair.Key, SequenceRange.Merge(pair.Value)))];

        int fixedLength = HeaderLength + (normalized.Count * AuthorHeaderLength);
        int rangeBudget = (MaxBodyLength - fixedLength) / RangeLength;
        int[] counts = new int[normalized.Count];
        int totalRanges = 0;
        while (totalRanges < rangeBudget)
        {
            bool grew = false;
            for (int index = 0; index < normalized.Count && totalRanges < rangeBudget; index++)
            {
                if (counts[index] < normalized[index].Ranges.Count && counts[index] < MaxRangesPerAuthor)
                {
                    counts[index]++;
                    totalRanges++;
                    grew = true;
                }
            }

            if (!grew)
            {
                break;
            }
        }

        GroupSummaryAuthor[] selected = new GroupSummaryAuthor[normalized.Count];
        for (int index = 0; index < normalized.Count; index++)
        {
            List<SequenceRange> ranges = normalized[index].Ranges;
            int take = counts[index];
            SequenceRange[] kept = new SequenceRange[take];
            ranges.CopyTo(ranges.Count - take, kept, 0, take);
            selected[index] = GroupSummaryAuthor.CreateTrusted(normalized[index].Key, kept);
        }

        return new GroupSummary(groupId, epoch, selected, fixedLength + (totalRanges * RangeLength));
    }

    public bool Contains(Ed25519PublicKey authorKey, long sequence)
    {
        ArgumentNullException.ThrowIfNull(authorKey);

        int low = 0;
        int high = authors.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int order = GroupRecord.Compare(authors[middle].AuthorKey, authorKey);
            if (order < 0)
            {
                low = middle + 1;
            }
            else if (order > 0)
            {
                high = middle - 1;
            }
            else
            {
                return authors[middle].Contains(sequence);
            }
        }

        return false;
    }

    public bool Equals(GroupSummary? other) =>
        other is not null
        && GroupId.Equals(other.GroupId)
        && Epoch == other.Epoch
        && authors.Length == other.authors.Length
        && authors.AsSpan().SequenceEqual(other.authors);

    public override bool Equals(object? obj) => obj is GroupSummary other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GroupId);
        hash.Add(Epoch);
        foreach (GroupSummaryAuthor author in authors)
        {
            hash.Add(author);
        }

        return hash.ToHashCode();
    }

    internal static bool TryDecodeBody(ReadOnlySpan<byte> body, [NotNullWhen(true)] out GroupSummary? summary)
    {
        summary = null;
        if (body.Length is < HeaderLength or > MaxBodyLength
            || !GroupId.TryCreate(body[..GroupId.Length], out GroupId? groupId))
        {
            return false;
        }

        uint epoch = BinaryPrimitives.ReadUInt32BigEndian(body[GroupId.Length..]);
        int authorCount = body[GroupId.Length + sizeof(uint)];
        if (epoch is 0 or > GroupRecord.MaxEpoch || authorCount > MaxAuthors)
        {
            return false;
        }

        int offset = HeaderLength;
        GroupSummaryAuthor[] decodedAuthors = authorCount == 0 ? [] : new GroupSummaryAuthor[authorCount];
        for (int authorIndex = 0; authorIndex < authorCount; authorIndex++)
        {
            if (offset + AuthorHeaderLength > body.Length
                || !Ed25519PublicKey.TryCreate(body.Slice(offset, Ed25519PublicKey.Length), out Ed25519PublicKey? authorKey))
            {
                return false;
            }

            if (authorIndex > 0 && GroupRecord.Compare(decodedAuthors[authorIndex - 1].AuthorKey, authorKey) >= 0)
            {
                return false;
            }

            offset += Ed25519PublicKey.Length;
            int rangeCount = body[offset++];
            if (rangeCount == 0 || offset + (rangeCount * RangeLength) > body.Length)
            {
                return false;
            }

            SequenceRange[] ranges = new SequenceRange[rangeCount];
            for (int rangeIndex = 0; rangeIndex < rangeCount; rangeIndex++)
            {
                long first = BinaryPrimitives.ReadInt64BigEndian(body.Slice(offset, sizeof(long)));
                long last = BinaryPrimitives.ReadInt64BigEndian(body.Slice(offset + sizeof(long), sizeof(long)));
                if (first < 1 || last < first)
                {
                    return false;
                }

                if (rangeIndex > 0 && (ranges[rangeIndex - 1].Last == long.MaxValue || first <= ranges[rangeIndex - 1].Last + 1))
                {
                    return false;
                }

                ranges[rangeIndex] = new SequenceRange(first, last);
                offset += RangeLength;
            }

            decodedAuthors[authorIndex] = GroupSummaryAuthor.CreateTrusted(authorKey, ranges);
        }

        if (offset != body.Length)
        {
            return false;
        }

        summary = new GroupSummary(groupId, (int)epoch, decodedAuthors, body.Length);
        return true;
    }

    internal override void WriteBody(Span<byte> body)
    {
        GroupId.Bytes.CopyTo(body);
        BinaryPrimitives.WriteUInt32BigEndian(body[GroupId.Length..], (uint)Epoch);
        body[GroupId.Length + sizeof(uint)] = (byte)authors.Length;

        int offset = HeaderLength;
        foreach (GroupSummaryAuthor author in authors)
        {
            author.AuthorKey.Bytes.CopyTo(body.Slice(offset, Ed25519PublicKey.Length));
            offset += Ed25519PublicKey.Length;
            body[offset++] = (byte)author.Ranges.Count;
            foreach (SequenceRange range in author.Ranges)
            {
                BinaryPrimitives.WriteInt64BigEndian(body.Slice(offset, sizeof(long)), range.First);
                BinaryPrimitives.WriteInt64BigEndian(body.Slice(offset + sizeof(long), sizeof(long)), range.Last);
                offset += RangeLength;
            }
        }
    }

    private sealed class AuthorOrder : IComparer<Ed25519PublicKey>
    {
        public static AuthorOrder Instance { get; } = new();

        public int Compare(Ed25519PublicKey? x, Ed25519PublicKey? y) =>
            x is null ? (y is null ? 0 : -1) : y is null ? 1 : GroupRecord.Compare(x, y);
    }
}
