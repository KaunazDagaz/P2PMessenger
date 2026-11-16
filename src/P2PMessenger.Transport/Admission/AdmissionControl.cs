using System.Diagnostics.CodeAnalysis;
using System.Net;
using P2PMessenger.Transport.Connections;

namespace P2PMessenger.Transport.Admission;

internal sealed class AdmissionControl
{
    private readonly AdmissionLimits limits;
    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private readonly Dictionary<IPAddress, AdmissionSource> sources = [];
    private readonly TokenBucket handshakes;
    private int pending;

    public AdmissionControl(AdmissionLimits limits, TimeProvider timeProvider)
    {
        this.limits = limits;
        this.timeProvider = timeProvider;
        handshakes = new TokenBucket(limits.HandshakesPerSecond, limits.HandshakeBurst, timeProvider.GetTimestamp());
    }

    public int Pending
    {
        get
        {
            lock (gate)
            {
                return pending;
            }
        }
    }

    public int TrackedSources
    {
        get
        {
            lock (gate)
            {
                return sources.Count;
            }
        }
    }

    public bool TryAdmit(IPAddress? source, [NotNullWhen(true)] out AdmissionSlot? slot, out AdmissionRefusal refusal)
    {
        lock (gate)
        {
            long now = timeProvider.GetTimestamp();
            refusal = Check(source, now, out AdmissionSource? state);
            if (refusal != AdmissionRefusal.None)
            {
                slot = null;
                return false;
            }

            handshakes.Take();
            pending++;
            if (state is not null)
            {
                state.Handshakes.Take();
                state.Pending++;
            }

            slot = new AdmissionSlot(this, state);
            return true;
        }
    }

    public void Release(AdmissionSource? source)
    {
        lock (gate)
        {
            pending--;
            if (source is not null)
            {
                source.Pending--;
            }
        }
    }

    private AdmissionRefusal Check(IPAddress? source, long now, out AdmissionSource? state)
    {
        state = null;
        if (pending >= limits.MaxPendingConnections)
        {
            return AdmissionRefusal.TooManyPending;
        }

        long frequency = timeProvider.TimestampFrequency;
        if (source is not null)
        {
            IPAddress key = source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source;
            if (!sources.TryGetValue(key, out state))
            {
                if (sources.Count >= limits.MaxTrackedSources)
                {
                    ForgetIdleSources(now, frequency);
                    if (sources.Count >= limits.MaxTrackedSources)
                    {
                        return AdmissionRefusal.TooManySources;
                    }
                }

                state = new AdmissionSource(new TokenBucket(limits.HandshakesPerSecondPerSource, limits.HandshakeBurstPerSource, now));
                sources.Add(key, state);
            }

            if (state.Pending >= limits.MaxPendingPerSource)
            {
                return AdmissionRefusal.TooManyPendingFromSource;
            }

            if (!state.Handshakes.HasToken(now, frequency))
            {
                return AdmissionRefusal.TooManyHandshakesFromSource;
            }
        }

        return handshakes.HasToken(now, frequency) ? AdmissionRefusal.None : AdmissionRefusal.TooManyHandshakes;
    }

    private void ForgetIdleSources(long now, long frequency)
    {
        List<IPAddress> idle = [];
        foreach (KeyValuePair<IPAddress, AdmissionSource> entry in sources)
        {
            if (entry.Value.Pending == 0 && entry.Value.Handshakes.IsFull(now, frequency))
            {
                idle.Add(entry.Key);
            }
        }

        foreach (IPAddress address in idle)
        {
            sources.Remove(address);
        }
    }
}
