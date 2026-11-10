using System.Globalization;
using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Own;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class InteropChecks
{
    private static readonly int[] TransportPayloadLengths = [0, 1, 1000, NoiseTransport.MaxPayloadLength];

    public static void Run(CheckReport report, INoiseCandidate first, INoiseCandidate second)
    {
        foreach (NoiseProtocol protocol in NoiseProtocol.All)
        {
            foreach ((INoiseCandidate initiator, INoiseCandidate responder) in new[] { (first, second), (second, first) })
            {
                report.Run(
                    $"{protocol.Name}, {initiator.Name} calls {responder.Name}",
                    () => Check(protocol, initiator, responder));
            }
        }
    }

    private static CheckOutcome Check(NoiseProtocol protocol, INoiseCandidate initiatorCandidate, INoiseCandidate responderCandidate)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        using INoiseParty initiator = setup.CreateInitiator(initiatorCandidate, protocol);
        using INoiseParty responder = setup.CreateResponder(responderCandidate, protocol);

        if (!Messages.Transfer(initiator, responder, RandomNumberGenerator.GetBytes(24), out string problem))
        {
            return CheckOutcome.Fail("message 1: " + problem);
        }

        if (!Messages.Transfer(responder, initiator, RandomNumberGenerator.GetBytes(24), out problem))
        {
            return CheckOutcome.Fail("message 2: " + problem);
        }

        if (initiator.HandshakeHash is not { } initiatorHash
            || responder.HandshakeHash is not { } responderHash
            || !initiatorHash.AsSpan().SequenceEqual(responderHash))
        {
            return CheckOutcome.Fail("the handshake hashes differ or are missing");
        }

        foreach (int length in TransportPayloadLengths)
        {
            if (!Messages.Transfer(initiator, responder, RandomNumberGenerator.GetBytes(length), out problem)
                || !Messages.Transfer(responder, initiator, RandomNumberGenerator.GetBytes(length), out problem))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"a transport message of {length} bytes: {problem}"));
            }
        }

        return CheckOutcome.Pass(string.Create(
            CultureInfo.InvariantCulture,
            $"same handshake hash; {TransportPayloadLengths.Length * 2} transport messages of 0 to {NoiseTransport.MaxPayloadLength} bytes, both ways"));
    }
}
