using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Application.Groups;

internal sealed class DeletedRanges
{
    private readonly Dictionary<Ed25519PublicKey, List<(long First, long Last)>> byAuthor;

    private DeletedRanges(Dictionary<Ed25519PublicKey, List<(long First, long Last)>> byAuthor)
    {
        this.byAuthor = byAuthor;
    }

    public static DeletedRanges Empty { get; } = new([]);

    public bool IsEmpty => byAuthor.Count == 0;

    public static DeletedRanges From(IEnumerable<DeletedRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        Dictionary<Ed25519PublicKey, List<(long First, long Last)>> byAuthor = [];
        foreach (DeletedRange range in ranges)
        {
            if (range.First < 1 || range.Last < range.First)
            {
                throw new ArgumentException("A deleted range starts at 1 or later and ends at or after its start.", nameof(ranges));
            }

            Add(byAuthor, range.AuthorKey, range.First, range.Last);
        }

        return new DeletedRanges(Merged(byAuthor));
    }

    public bool Contains(Ed25519PublicKey authorKey, long sequence) =>
        byAuthor.TryGetValue(authorKey, out List<(long First, long Last)>? ranges)
        && ranges.Exists(range => sequence >= range.First && sequence <= range.Last);

    public long HighestOf(Ed25519PublicKey authorKey) =>
        byAuthor.TryGetValue(authorKey, out List<(long First, long Last)>? ranges) && ranges.Count > 0 ? ranges[^1].Last : 0;

    public DeletedRanges With(IEnumerable<(Ed25519PublicKey AuthorKey, long Sequence)> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);

        Dictionary<Ed25519PublicKey, List<(long First, long Last)>> copy = byAuthor.ToDictionary(static pair => pair.Key, static pair => new List<(long First, long Last)>(pair.Value));
        foreach ((Ed25519PublicKey authorKey, long sequence) in sequences)
        {
            Add(copy, authorKey, sequence, sequence);
        }

        return new DeletedRanges(Merged(copy));
    }

    public IReadOnlyList<DeletedRange> ToList() =>
    [
        .. byAuthor
            .OrderBy(static pair => pair.Key.Bytes.ToArray(), ByteOrder.Instance)
            .SelectMany(static pair => pair.Value.Select(range => new DeletedRange(pair.Key, range.First, range.Last))),
    ];

    private static void Add(Dictionary<Ed25519PublicKey, List<(long First, long Last)>> byAuthor, Ed25519PublicKey authorKey, long first, long last)
    {
        ArgumentNullException.ThrowIfNull(authorKey);

        if (!byAuthor.TryGetValue(authorKey, out List<(long First, long Last)>? ranges))
        {
            ranges = [];
            byAuthor.Add(authorKey, ranges);
        }

        ranges.Add((first, last));
    }

    private static Dictionary<Ed25519PublicKey, List<(long First, long Last)>> Merged(Dictionary<Ed25519PublicKey, List<(long First, long Last)>> byAuthor)
    {
        Dictionary<Ed25519PublicKey, List<(long First, long Last)>> merged = [];
        foreach ((Ed25519PublicKey authorKey, List<(long First, long Last)> ranges) in byAuthor)
        {
            List<(long First, long Last)> result = [];
            foreach ((long first, long last) in ranges.OrderBy(static range => range.First))
            {
                if (result.Count > 0 && first <= result[^1].Last + 1)
                {
                    result[^1] = (result[^1].First, Math.Max(result[^1].Last, last));
                }
                else
                {
                    result.Add((first, last));
                }
            }

            if (result.Count > 0)
            {
                merged.Add(authorKey, result);
            }
        }

        return merged;
    }

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static ByteOrder Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
