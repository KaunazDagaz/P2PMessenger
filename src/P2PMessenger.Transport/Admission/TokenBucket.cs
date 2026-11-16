namespace P2PMessenger.Transport.Admission;

internal sealed class TokenBucket
{
    private readonly double tokensPerSecond;
    private readonly double capacity;
    private double tokens;
    private long updatedAt;

    public TokenBucket(double tokensPerSecond, double capacity, long now)
    {
        this.tokensPerSecond = tokensPerSecond;
        this.capacity = capacity;
        tokens = capacity;
        updatedAt = now;
    }

    public bool HasToken(long now, long frequency)
    {
        Refill(now, frequency);
        return tokens >= 1;
    }

    public bool IsFull(long now, long frequency)
    {
        Refill(now, frequency);
        return tokens >= capacity;
    }

    public void Take() => tokens -= 1;

    private void Refill(long now, long frequency)
    {
        long elapsed = now - updatedAt;
        if (elapsed <= 0)
        {
            return;
        }

        tokens = Math.Min(capacity, tokens + (elapsed * tokensPerSecond / frequency));
        updatedAt = now;
    }
}
