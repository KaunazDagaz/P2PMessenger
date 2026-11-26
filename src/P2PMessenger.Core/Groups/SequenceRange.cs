namespace P2PMessenger.Core.Groups;

public readonly record struct SequenceRange
{
    public SequenceRange(long first, long last)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);

        First = first;
        Last = last;
    }

    public long First { get; }

    public long Last { get; }

    public bool Contains(long sequence) => sequence >= First && sequence <= Last;

    public bool Covers(SequenceRange other) => First <= other.First && Last >= other.Last;

    public static bool TryCreate(long first, long last, out SequenceRange range)
    {
        if (first < 1 || last < first)
        {
            range = default;
            return false;
        }

        range = new SequenceRange(first, last);
        return true;
    }

    public static List<SequenceRange> Merge(IEnumerable<SequenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        List<SequenceRange> sorted = [.. ranges];
        if (sorted.Count == 0)
        {
            return [];
        }

        foreach (SequenceRange range in sorted)
        {
            if (range.First < 1 || range.Last < range.First)
            {
                throw new ArgumentException("A sequence range starts at 1 or later and ends at or after its start.", nameof(ranges));
            }
        }

        sorted.Sort(static (left, right) => left.First.CompareTo(right.First));
        List<SequenceRange> merged = new(sorted.Count);
        foreach (SequenceRange range in sorted)
        {
            if (merged.Count > 0 && (merged[^1].Last == long.MaxValue || range.First <= merged[^1].Last + 1))
            {
                merged[^1] = new SequenceRange(merged[^1].First, Math.Max(merged[^1].Last, range.Last));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
