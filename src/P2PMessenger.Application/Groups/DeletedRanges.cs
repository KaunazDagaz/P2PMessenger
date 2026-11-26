using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;

namespace P2PMessenger.Application.Groups;

internal sealed class DeletedRanges
{
    private readonly Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor;

    private DeletedRanges(Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor)
    {
        this.byAuthor = byAuthor;
    }

    public static DeletedRanges Empty { get; } = new([]);

    public bool IsEmpty => byAuthor.Count == 0;

    public static DeletedRanges From(IEnumerable<AuthorSequenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor = [];
        foreach (AuthorSequenceRange range in ranges)
        {
            ArgumentNullException.ThrowIfNull(range);
            Add(byAuthor, range.AuthorKey, range.Range);
        }

        return new DeletedRanges(Merged(byAuthor));
    }

    public bool Contains(Ed25519PublicKey authorKey, long sequence) =>
        byAuthor.TryGetValue(authorKey, out List<SequenceRange>? ranges)
        && ranges.Exists(range => range.Contains(sequence));

    public long HighestOf(Ed25519PublicKey authorKey) =>
        byAuthor.TryGetValue(authorKey, out List<SequenceRange>? ranges) && ranges.Count > 0 ? ranges[^1].Last : 0;

    public DeletedRanges With(IEnumerable<(Ed25519PublicKey AuthorKey, long Sequence)> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);

        Dictionary<Ed25519PublicKey, List<SequenceRange>> copy = byAuthor.ToDictionary(static pair => pair.Key, static pair => new List<SequenceRange>(pair.Value));
        foreach ((Ed25519PublicKey authorKey, long sequence) in sequences)
        {
            Add(copy, authorKey, new SequenceRange(sequence, sequence));
        }

        return new DeletedRanges(Merged(copy));
    }

    public IReadOnlyList<AuthorSequenceRange> ToList() =>
    [
        .. byAuthor
            .OrderBy(static pair => pair.Key.Bytes.ToArray(), ByteOrder.Instance)
            .SelectMany(static pair => pair.Value.Select(range => new AuthorSequenceRange(pair.Key, range))),
    ];

    private static void Add(Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor, Ed25519PublicKey authorKey, SequenceRange range)
    {
        ArgumentNullException.ThrowIfNull(authorKey);

        if (!byAuthor.TryGetValue(authorKey, out List<SequenceRange>? ranges))
        {
            ranges = [];
            byAuthor.Add(authorKey, ranges);
        }

        ranges.Add(range);
    }

    private static Dictionary<Ed25519PublicKey, List<SequenceRange>> Merged(Dictionary<Ed25519PublicKey, List<SequenceRange>> byAuthor)
    {
        Dictionary<Ed25519PublicKey, List<SequenceRange>> merged = [];
        foreach ((Ed25519PublicKey authorKey, List<SequenceRange> ranges) in byAuthor)
        {
            List<SequenceRange> result = SequenceRange.Merge(ranges);
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
