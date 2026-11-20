namespace P2PMessenger.Application.LocalData;

public readonly record struct ProtectResult(KeyStoreOutcome Outcome, ReadOnlyMemory<byte> ProtectedKey)
{
    public bool Succeeded => Outcome == KeyStoreOutcome.Succeeded;

    public static ProtectResult Success(ReadOnlyMemory<byte> protectedKey)
    {
        if (protectedKey.IsEmpty)
        {
            throw new ArgumentException("A protected key is never empty.", nameof(protectedKey));
        }

        return new ProtectResult(KeyStoreOutcome.Succeeded, protectedKey);
    }

    public static ProtectResult Failure(KeyStoreOutcome outcome)
    {
        if (outcome == KeyStoreOutcome.Succeeded || !Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A failure needs a failed outcome.");
        }

        return new ProtectResult(outcome, ReadOnlyMemory<byte>.Empty);
    }
}
