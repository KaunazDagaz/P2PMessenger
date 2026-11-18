namespace P2PMessenger.Application.Tests;

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset now;

    public ManualTime(DateTimeOffset start)
    {
        now = start;
    }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
