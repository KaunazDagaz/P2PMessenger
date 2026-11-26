using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.Copy;

public sealed class StartReceiveResult
{
    private StartReceiveResult(PcCopyReceiveProblem problem, PcCopyReceiver? receiver)
    {
        Problem = problem;
        Receiver = receiver;
    }

    public PcCopyReceiveProblem Problem { get; }

    public PcCopyReceiver? Receiver { get; }

    [MemberNotNullWhen(true, nameof(Receiver))]
    public bool Started => Receiver is not null;

    public static StartReceiveResult Ok(PcCopyReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        return new StartReceiveResult(PcCopyReceiveProblem.None, receiver);
    }

    public static StartReceiveResult Failed(PcCopyReceiveProblem problem)
    {
        if (problem == PcCopyReceiveProblem.None)
        {
            throw new ArgumentOutOfRangeException(nameof(problem), problem, "A failure must carry a problem.");
        }

        return new StartReceiveResult(problem, null);
    }
}
