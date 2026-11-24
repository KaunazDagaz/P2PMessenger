namespace P2PMessenger.Transport.Tests.Tor;

internal static class Eventually
{
    public static async Task<bool> TrueAsync(Func<bool> condition, CancellationToken cancellationToken, TimeSpan? limit = null)
    {
        var deadline = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20, cancellationToken);
        }

        return condition();
    }
}
