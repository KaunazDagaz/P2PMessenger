namespace P2PMessenger.Storage.Protection;

public readonly record struct PassphraseKdf(long MemoryKib, long Passes)
{
    public const long MinMemoryKib = 8;
    public const long MaxMemoryKib = 1_048_576;
    public const long MinPasses = 1;
    public const long MaxPasses = 16;

    public static PassphraseKdf Default { get; } = new(65_536, 3);

    public bool IsAcceptable =>
        MemoryKib is >= MinMemoryKib and <= MaxMemoryKib
        && Passes is >= MinPasses and <= MaxPasses;
}
