using P2PMessenger.Core.Crypto;

namespace P2PMessenger.Core.Groups;

public sealed record AuthorSequenceRange
{
    public AuthorSequenceRange(Ed25519PublicKey authorKey, SequenceRange range)
    {
        ArgumentNullException.ThrowIfNull(authorKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(range.First, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(range.Last, range.First);

        AuthorKey = authorKey;
        Range = range;
    }

    public AuthorSequenceRange(Ed25519PublicKey authorKey, long first, long last)
        : this(authorKey, new SequenceRange(first, last))
    {
    }

    public Ed25519PublicKey AuthorKey { get; }

    public SequenceRange Range { get; }

    public long First => Range.First;

    public long Last => Range.Last;
}
