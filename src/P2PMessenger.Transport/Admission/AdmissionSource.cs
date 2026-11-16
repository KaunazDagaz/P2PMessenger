namespace P2PMessenger.Transport.Admission;

internal sealed class AdmissionSource
{
    public AdmissionSource(TokenBucket handshakes)
    {
        Handshakes = handshakes;
    }

    public TokenBucket Handshakes { get; }

    public int Pending { get; set; }
}
