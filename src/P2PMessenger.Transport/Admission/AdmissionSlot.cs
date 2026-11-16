namespace P2PMessenger.Transport.Admission;

internal sealed class AdmissionSlot : IDisposable
{
    private readonly AdmissionControl owner;
    private readonly AdmissionSource? source;
    private int released;

    public AdmissionSlot(AdmissionControl owner, AdmissionSource? source)
    {
        this.owner = owner;
        this.source = source;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
        {
            owner.Release(source);
        }
    }
}
