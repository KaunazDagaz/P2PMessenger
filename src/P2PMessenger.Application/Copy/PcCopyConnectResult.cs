using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Copy;

public sealed class PcCopyConnectResult
{
    private PcCopyConnectResult(PcCopyConnectOutcome outcome, PcCopySender? sender)
    {
        Outcome = outcome;
        Sender = sender;
    }

    public PcCopyConnectOutcome Outcome { get; }

    public PcCopySender? Sender { get; }

    [MemberNotNullWhen(true, nameof(Sender))]
    public bool Connected => Sender is not null;

    public static PcCopyConnectResult Ok(PcCopySender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        return new PcCopyConnectResult(PcCopyConnectOutcome.Connected, sender);
    }

    public static PcCopyConnectResult Failed(PcCopyConnectOutcome outcome)
    {
        if (outcome == PcCopyConnectOutcome.Connected)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A failure cannot have Connected outcome.");
        }

        return new PcCopyConnectResult(outcome, null);
    }
}
