using NSec.Cryptography;
using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Own;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Noise.Checks;

internal sealed class PeerSetup
{
    private PeerSetup(byte[] prologue, (byte[] Private, byte[] Public) initiator, (byte[] Private, byte[] Public) responder)
    {
        Prologue = prologue;
        InitiatorPrivate = initiator.Private;
        InitiatorPublic = initiator.Public;
        ResponderPrivate = responder.Private;
        ResponderPublic = responder.Public;
    }

    public byte[] Prologue { get; }

    public byte[] InitiatorPrivate { get; }

    public byte[] InitiatorPublic { get; }

    public byte[] ResponderPrivate { get; }

    public byte[] ResponderPublic { get; }

    public static PeerSetup CreateRandom()
    {
        return new PeerSetup(RandomNumberGenerator.GetBytes(16), NewKeyPair(), NewKeyPair());
    }

    public static (byte[] Private, byte[] Public) NewKeyPair()
    {
        using Key key = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return (key.Export(KeyBlobFormat.RawPrivateKey), key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public INoiseParty CreateInitiator(INoiseCandidate candidate, NoiseProtocol protocol, ReadOnlySpan<byte> fixedEphemeral = default)
    {
        return candidate.Create(protocol, true, Prologue, InitiatorPrivate, ResponderPublic, fixedEphemeral);
    }

    public INoiseParty CreateResponder(INoiseCandidate candidate, NoiseProtocol protocol, ReadOnlySpan<byte> fixedEphemeral = default)
    {
        return candidate.Create(protocol, false, Prologue, ResponderPrivate, InitiatorPublic, fixedEphemeral);
    }
}
