namespace P2PMessenger.Application.LocalData;

public readonly record struct PassphraseChangeResult(PassphraseChangeOutcome Outcome, LocalDataProblem Problem)
{
    public bool IsChanged => Outcome == PassphraseChangeOutcome.Changed;

    public static PassphraseChangeResult Of(PassphraseChangeOutcome outcome) => new(outcome, LocalDataProblem.None);

    public static PassphraseChangeResult KeyStoreFailed(LocalDataProblem problem) => new(PassphraseChangeOutcome.KeyStoreFailed, problem);
}
