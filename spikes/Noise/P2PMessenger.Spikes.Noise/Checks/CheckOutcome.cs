namespace P2PMessenger.Spikes.Noise.Checks;

public sealed record CheckOutcome(CheckStatus Status, string Detail)
{
    public static CheckOutcome Pass(string detail) => new(CheckStatus.Pass, detail);

    public static CheckOutcome Fail(string detail) => new(CheckStatus.Fail, detail);

    public static CheckOutcome Skip(string detail) => new(CheckStatus.Skip, detail);

    public static CheckOutcome Info(string detail) => new(CheckStatus.Info, detail);
}
