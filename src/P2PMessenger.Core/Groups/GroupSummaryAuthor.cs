using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Groups;

public sealed class GroupSummaryAuthor : IEquatable<GroupSummaryAuthor>
{
    private readonly SequenceRange[] ranges;

    public GroupSummaryAuthor(Ed25519PublicKey authorKey, IEnumerable<SequenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(authorKey);
        ArgumentNullException.ThrowIfNull(ranges);

        SequenceRange[] list = [.. ranges];
        if (list.Length is 0 or > GroupSummary.MaxRangesPerAuthor)
        {
            throw new ArgumentException($"An author summary carries 1 to {GroupSummary.MaxRangesPerAuthor} ranges.", nameof(ranges));
        }

        for (int index = 0; index < list.Length; index++)
        {
            if (list[index].First < 1 || list[index].Last < list[index].First)
            {
                throw new ArgumentException("A sequence range starts at 1 or later and ends at or after its start.", nameof(ranges));
            }

            if (index > 0 && (list[index - 1].Last == long.MaxValue || list[index].First <= list[index - 1].Last + 1))
            {
                throw new ArgumentException("Ranges for an author must be sorted and neither overlap nor touch.", nameof(ranges));
            }
        }

        AuthorKey = authorKey;
        this.ranges = list;
    }

    private GroupSummaryAuthor(Ed25519PublicKey authorKey, SequenceRange[] ranges)
    {
        AuthorKey = authorKey;
        this.ranges = ranges;
    }

    public Ed25519PublicKey AuthorKey { get; }

    public IReadOnlyList<SequenceRange> Ranges => ranges;

    public bool Contains(long sequence)
    {
        int low = 0;
        int high = ranges.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            SequenceRange range = ranges[middle];
            if (sequence < range.First)
            {
                high = middle - 1;
            }
            else if (sequence > range.Last)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(GroupSummaryAuthor? other) =>
        other is not null && AuthorKey.Equals(other.AuthorKey) && ranges.AsSpan().SequenceEqual(other.ranges);

    public override bool Equals(object? obj) => obj is GroupSummaryAuthor other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AuthorKey);
        foreach (SequenceRange range in ranges)
        {
            hash.Add(range);
        }

        return hash.ToHashCode();
    }

    internal static GroupSummaryAuthor CreateTrusted(Ed25519PublicKey authorKey, SequenceRange[] ranges) =>
        new(authorKey, ranges);
}
