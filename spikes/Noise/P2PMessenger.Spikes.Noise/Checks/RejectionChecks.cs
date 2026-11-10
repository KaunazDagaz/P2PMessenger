using System.Globalization;
using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Own;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Noise.Checks;

internal static class RejectionChecks
{
    private const int PayloadLength = 16;

    public static void Run(CheckReport report, INoiseCandidate candidate, NoiseProtocol protocol)
    {
        bool canFixEphemeral = candidate.CanFixEphemeral(out string reason);
        string prefix = candidate.Name + " refuses ";
        report.Run(prefix + "message 1 from a key that is not the expected contact", () => UnknownInitiator(candidate, protocol));
        report.Run(prefix + "message 1 meant for another key", () => OtherResponder(candidate, protocol));
        report.Run(prefix + "message 1 with another prologue", () => OtherPrologue(candidate, protocol));
        report.Run(prefix + "every single-bit change of message 1", () => FlippedFirstMessage(candidate, protocol));
        report.Run(
            prefix + "every single-bit change of message 2",
            () => canFixEphemeral ? FlippedSecondMessage(candidate, protocol) : CheckOutcome.Skip("needs a fixed ephemeral key, and " + reason));
        report.Run(prefix + "every single-bit change of a transport message", () => FlippedTransportMessage(candidate, protocol));
        report.Run(prefix + "shortened and lengthened messages", () => WrongLengths(candidate, protocol));
        report.Run(prefix + "replayed and reordered transport messages", () => ReplayedAndReordered(candidate, protocol));
        report.Run(prefix + "messages over 65,535 bytes", () => Oversized(candidate, protocol));
    }

    private static CheckOutcome UnknownInitiator(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        (byte[] strangerPrivate, _) = PeerSetup.NewKeyPair();
        using INoiseParty stranger = candidate.Create(protocol, true, setup.Prologue, strangerPrivate, setup.ResponderPublic);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        byte[] message = Messages.Write(stranger, RandomNumberGenerator.GetBytes(PayloadLength));
        return Messages.TryRead(responder, message, out _)
            ? CheckOutcome.Fail("the responder accepted it")
            : CheckOutcome.Pass("the responder refused it");
    }

    private static CheckOutcome OtherResponder(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        (_, byte[] otherPublic) = PeerSetup.NewKeyPair();
        using INoiseParty initiator = candidate.Create(protocol, true, setup.Prologue, setup.InitiatorPrivate, otherPublic);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        byte[] message = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        return Messages.TryRead(responder, message, out _)
            ? CheckOutcome.Fail("a responder with a key other than the one the caller expects read it")
            : CheckOutcome.Pass("a responder with a key other than the one the caller expects cannot read it");
    }

    private static CheckOutcome OtherPrologue(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty responder = candidate.Create(protocol, false, RandomNumberGenerator.GetBytes(16), setup.ResponderPrivate, setup.InitiatorPublic);
        byte[] message = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        return Messages.TryRead(responder, message, out _)
            ? CheckOutcome.Fail("the responder accepted it")
            : CheckOutcome.Pass("the responder refused it, so a protocol version in the prologue is checked by the first message");
    }

    private static CheckOutcome FlippedFirstMessage(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        byte[] message;
        using (INoiseParty initiator = setup.CreateInitiator(candidate, protocol))
        {
            message = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        }

        int bits = message.Length * 8;
        for (int bit = 0; bit < bits; bit++)
        {
            using INoiseParty responder = setup.CreateResponder(candidate, protocol);
            if (Messages.TryRead(responder, Messages.FlipBit(message, bit), out _))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"bit {bit} changed and still accepted"));
            }
        }

        using INoiseParty control = setup.CreateResponder(candidate, protocol);
        return Messages.TryRead(control, message, out _)
            ? CheckOutcome.Pass(string.Create(CultureInfo.InvariantCulture, $"all {bits} changed copies refused; the unchanged message is accepted"))
            : CheckOutcome.Fail("the unchanged message was refused");
    }

    private static CheckOutcome FlippedSecondMessage(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        byte[] ephemeral = PeerSetup.NewKeyPair().Private;
        byte[] firstPayload = RandomNumberGenerator.GetBytes(PayloadLength);
        byte[] first;
        byte[] second;
        using (INoiseParty initiator = setup.CreateInitiator(candidate, protocol, ephemeral))
        using (INoiseParty responder = setup.CreateResponder(candidate, protocol))
        {
            first = Messages.Write(initiator, firstPayload);
            if (!Messages.TryRead(responder, first, out _))
            {
                return CheckOutcome.Fail("the responder refused message 1");
            }

            second = Messages.Write(responder, RandomNumberGenerator.GetBytes(PayloadLength));
        }

        int bits = second.Length * 8;
        for (int bit = 0; bit < bits; bit++)
        {
            using INoiseParty initiator = setup.CreateInitiator(candidate, protocol, ephemeral);
            if (!Messages.Write(initiator, firstPayload).AsSpan().SequenceEqual(first))
            {
                return CheckOutcome.Fail("the same ephemeral key did not give the same message 1");
            }

            if (Messages.TryRead(initiator, Messages.FlipBit(second, bit), out _))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"bit {bit} changed and still accepted"));
            }
        }

        using INoiseParty control = setup.CreateInitiator(candidate, protocol, ephemeral);
        _ = Messages.Write(control, firstPayload);
        return Messages.TryRead(control, second, out _)
            ? CheckOutcome.Pass(string.Create(CultureInfo.InvariantCulture, $"all {bits} changed copies refused; the unchanged message is accepted"))
            : CheckOutcome.Fail("the unchanged message was refused");
    }

    private static CheckOutcome FlippedTransportMessage(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        CompleteHandshake(initiator, responder);

        byte[] message = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        int bits = message.Length * 8;
        for (int bit = 0; bit < bits; bit++)
        {
            if (Messages.TryRead(responder, Messages.FlipBit(message, bit), out _))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"bit {bit} changed and still accepted"));
            }
        }

        return Messages.TryRead(responder, message, out _)
            ? CheckOutcome.Pass(string.Create(CultureInfo.InvariantCulture, $"all {bits} changed copies refused; the unchanged message is still accepted after them"))
            : CheckOutcome.Fail("the unchanged message was refused after the changed copies");
    }

    private static CheckOutcome WrongLengths(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        byte[] first;
        using (INoiseParty writer = setup.CreateInitiator(candidate, protocol))
        {
            first = Messages.Write(writer, RandomNumberGenerator.GetBytes(PayloadLength));
        }

        for (int length = 0; length <= first.Length; length++)
        {
            using INoiseParty responder = setup.CreateResponder(candidate, protocol);
            byte[] candidateMessage = length < first.Length ? first[..length] : [.. first, 0];
            if (Messages.TryRead(responder, candidateMessage, out _))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"message 1 with {candidateMessage.Length} of {first.Length} bytes accepted"));
            }
        }

        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty receiver = setup.CreateResponder(candidate, protocol);
        CompleteHandshake(initiator, receiver);
        byte[] transport = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        for (int length = 0; length <= transport.Length; length++)
        {
            byte[] candidateMessage = length < transport.Length ? transport[..length] : [.. transport, 0];
            if (Messages.TryRead(receiver, candidateMessage, out _))
            {
                return CheckOutcome.Fail(string.Create(CultureInfo.InvariantCulture, $"a transport message with {candidateMessage.Length} of {transport.Length} bytes accepted"));
            }
        }

        return Messages.TryRead(receiver, transport, out _)
            ? CheckOutcome.Pass(string.Create(
                CultureInfo.InvariantCulture,
                $"{first.Length + 1} wrong lengths of message 1 and {transport.Length + 1} of a transport message refused"))
            : CheckOutcome.Fail("the unchanged transport message was refused");
    }

    private static CheckOutcome ReplayedAndReordered(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        CompleteHandshake(initiator, responder);

        byte[] first = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        byte[] second = Messages.Write(initiator, RandomNumberGenerator.GetBytes(PayloadLength));
        if (Messages.TryRead(responder, second, out _))
        {
            return CheckOutcome.Fail("the second message was accepted before the first");
        }

        if (!Messages.TryRead(responder, first, out _) || !Messages.TryRead(responder, second, out _))
        {
            return CheckOutcome.Fail("the messages were refused in their right order");
        }

        return Messages.TryRead(responder, first, out _) || Messages.TryRead(responder, second, out _)
            ? CheckOutcome.Fail("a replayed message was accepted")
            : CheckOutcome.Pass("a message read out of order and both messages read again are refused; in order they are accepted");
    }

    private static CheckOutcome Oversized(INoiseCandidate candidate, NoiseProtocol protocol)
    {
        PeerSetup setup = PeerSetup.CreateRandom();
        using (INoiseParty responder = setup.CreateResponder(candidate, protocol))
        {
            if (Messages.TryRead(responder, new byte[KkHandshake.MaxMessageLength + 1], out _))
            {
                return CheckOutcome.Fail("a 65,536-byte message 1 was accepted");
            }
        }

        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty receiver = setup.CreateResponder(candidate, protocol);
        CompleteHandshake(initiator, receiver);
        if (Messages.TryRead(receiver, new byte[KkHandshake.MaxMessageLength + 1], out _))
        {
            return CheckOutcome.Fail("a 65,536-byte transport message was accepted");
        }

        try
        {
            _ = Messages.Write(initiator, new byte[NoiseTransport.MaxPayloadLength + 1]);
            return CheckOutcome.Fail("a payload of 65,520 bytes was written");
        }
        catch (ArgumentException)
        {
            return CheckOutcome.Pass("65,536-byte messages are refused, and the writer refuses a 65,520-byte payload");
        }
    }

    private static void CompleteHandshake(INoiseParty initiator, INoiseParty responder)
    {
        if (!Messages.Transfer(initiator, responder, RandomNumberGenerator.GetBytes(PayloadLength), out string problem)
            || !Messages.Transfer(responder, initiator, RandomNumberGenerator.GetBytes(PayloadLength), out problem))
        {
            throw new InvalidOperationException("The handshake failed: " + problem);
        }
    }
}
