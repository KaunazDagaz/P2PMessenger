using P2PMessenger.Spikes.Noise.Own;

namespace P2PMessenger.Spikes.Noise.Candidates;

public interface INoiseCandidate
{
    string Name { get; }

    bool CanFixEphemeral(out string reason);

    INoiseParty Create(
        NoiseProtocol protocol,
        bool initiator,
        ReadOnlySpan<byte> prologue,
        ReadOnlySpan<byte> localStaticPrivateKey,
        ReadOnlySpan<byte> remoteStaticPublicKey,
        ReadOnlySpan<byte> fixedEphemeralPrivateKey = default);
}
