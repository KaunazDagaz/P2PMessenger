namespace P2PMessenger.Transport.Connections;

public sealed record AdmissionLimits
{
    public int MaxPendingConnections { get; init; } = 32;

    public int MaxPendingPerSource { get; init; } = 4;

    public double HandshakesPerSecond { get; init; } = 20;

    public int HandshakeBurst { get; init; } = 40;

    public double HandshakesPerSecondPerSource { get; init; } = 4;

    public int HandshakeBurstPerSource { get; init; } = 8;

    public int MaxTrackedSources { get; init; } = 1024;

    internal string? FindProblem()
    {
        if (MaxPendingConnections < 1
            || MaxPendingPerSource < 1
            || HandshakeBurst < 1
            || HandshakeBurstPerSource < 1
            || MaxTrackedSources < 1)
        {
            return "Every admission count must be at least 1.";
        }

        if (!IsPositiveRate(HandshakesPerSecond) || !IsPositiveRate(HandshakesPerSecondPerSource))
        {
            return "Every handshake rate must be a positive number.";
        }

        return null;
    }

    private static bool IsPositiveRate(double rate) => double.IsFinite(rate) && rate > 0;
}
