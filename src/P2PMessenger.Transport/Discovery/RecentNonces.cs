using System.Buffers.Binary;

namespace P2PMessenger.Transport.Discovery;

internal sealed class RecentNonces
{
    private readonly Dictionary<UInt128, long> expiries = [];
    private readonly long lifetime;
    private readonly int capacity;

    public RecentNonces(TimeSpan lifetime, long frequency, int capacity)
    {
        this.lifetime = (long)(lifetime.TotalSeconds * frequency);
        this.capacity = capacity;
    }

    public int Count => expiries.Count;

    public bool Contains(ReadOnlySpan<byte> nonce, long now) =>
        expiries.TryGetValue(Key(nonce), out long expiry) && expiry > now;

    public void Add(ReadOnlySpan<byte> nonce, long now)
    {
        if (expiries.Count >= capacity)
        {
            Prune(now);
            if (expiries.Count >= capacity)
            {
                long oldest = expiries.Values.Min();
                foreach (UInt128 stale in expiries.Where(entry => entry.Value == oldest).Select(static entry => entry.Key).ToList())
                {
                    expiries.Remove(stale);
                }
            }
        }

        expiries[Key(nonce)] = now + lifetime;
    }

    public void Prune(long now)
    {
        foreach (UInt128 stale in expiries.Where(entry => entry.Value <= now).Select(static entry => entry.Key).ToList())
        {
            expiries.Remove(stale);
        }
    }

    private static UInt128 Key(ReadOnlySpan<byte> nonce) => BinaryPrimitives.ReadUInt128BigEndian(nonce);
}
