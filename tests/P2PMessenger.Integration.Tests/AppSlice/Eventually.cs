namespace P2PMessenger.Integration.Tests.AppSlice;

internal static class Eventually
{
    public static async Task HoldsAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
