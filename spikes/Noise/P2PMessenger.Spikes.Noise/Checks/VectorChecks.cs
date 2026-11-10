using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Own;
using P2PMessenger.Spikes.Noise.Vectors;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class VectorChecks
{
    public static void Run(CheckReport report, IReadOnlyList<INoiseCandidate> candidates)
    {
        IReadOnlyList<NoiseVector> vectors = NoiseVector.LoadEmbedded();
        foreach (INoiseCandidate candidate in candidates)
        {
            bool canFixEphemeral = candidate.CanFixEphemeral(out string reason);
            foreach (NoiseVector vector in vectors)
            {
                report.Run(
                    $"{candidate.Name}, {vector.Source} vector {vector.ProtocolName}",
                    () => Check(candidate, vector, canFixEphemeral, reason));
            }
        }
    }

    private static CheckOutcome Check(INoiseCandidate candidate, NoiseVector vector, bool canFixEphemeral, string reason)
    {
        if (!NoiseProtocol.TryFind(vector.ProtocolName, out NoiseProtocol? protocol))
        {
            return CheckOutcome.Skip("the spike does not implement this protocol");
        }

        return canFixEphemeral ? CheckAll(candidate, protocol, vector) : CheckFirstMessage(candidate, protocol, vector, reason);
    }

    private static CheckOutcome CheckAll(INoiseCandidate candidate, NoiseProtocol protocol, NoiseVector vector)
    {
        using INoiseParty initiator = candidate.Create(
            protocol,
            true,
            vector.InitiatorPrologue.Span,
            vector.InitiatorStatic.Span,
            vector.InitiatorRemoteStatic.Span,
            vector.InitiatorEphemeral.Span);
        using INoiseParty responder = candidate.Create(
            protocol,
            false,
            vector.ResponderPrologue.Span,
            vector.ResponderStatic.Span,
            vector.ResponderRemoteStatic.Span,
            vector.ResponderEphemeral.Span);

        for (int index = 0; index < vector.Messages.Count; index++)
        {
            NoiseVectorMessage expected = vector.Messages[index];
            (INoiseParty sender, INoiseParty receiver) = index % 2 == 0 ? (initiator, responder) : (responder, initiator);
            byte[] written = Messages.Write(sender, expected.Payload.Span);
            if (!written.AsSpan().SequenceEqual(expected.Ciphertext.Span))
            {
                return CheckOutcome.Fail($"message {index + 1} differs from the vector");
            }

            if (!Messages.TryRead(receiver, expected.Ciphertext.Span, out byte[] payload))
            {
                return CheckOutcome.Fail($"message {index + 1} of the vector was refused");
            }

            if (!payload.AsSpan().SequenceEqual(expected.Payload.Span))
            {
                return CheckOutcome.Fail($"message {index + 1} decrypted to a different payload");
            }
        }

        byte[]? initiatorHash = initiator.HandshakeHash;
        byte[]? responderHash = responder.HandshakeHash;
        if (initiatorHash is null || responderHash is null)
        {
            return CheckOutcome.Fail("the handshake did not complete");
        }

        if (!initiatorHash.AsSpan().SequenceEqual(responderHash))
        {
            return CheckOutcome.Fail("the two handshake hashes differ");
        }

        if (vector.HandshakeHash is { } hash && !initiatorHash.AsSpan().SequenceEqual(hash.Span))
        {
            return CheckOutcome.Fail("the handshake hash differs from the vector");
        }

        string hashNote = vector.HandshakeHash is null ? "the vector has no handshake hash" : "and so does the handshake hash";
        return CheckOutcome.Pass($"all {vector.Messages.Count} messages match, both ways; {hashNote}");
    }

    private static CheckOutcome CheckFirstMessage(INoiseCandidate candidate, NoiseProtocol protocol, NoiseVector vector, string reason)
    {
        using INoiseParty responder = candidate.Create(
            protocol,
            false,
            vector.ResponderPrologue.Span,
            vector.ResponderStatic.Span,
            vector.ResponderRemoteStatic.Span);
        NoiseVectorMessage first = vector.Messages[0];
        if (!Messages.TryRead(responder, first.Ciphertext.Span, out byte[] payload) || !payload.AsSpan().SequenceEqual(first.Payload.Span))
        {
            return CheckOutcome.Fail("the responder did not read message 1 of the vector correctly");
        }

        return CheckOutcome.Skip("only message 1 checked (read correctly); the rest needs a fixed ephemeral key, and " + reason);
    }
}
