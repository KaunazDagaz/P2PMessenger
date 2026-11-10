using System.Diagnostics;
using System.Globalization;
using P2PMessenger.Spikes.Noise.Candidates;
using P2PMessenger.Spikes.Noise.Checks;
using P2PMessenger.Spikes.Noise.Own;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

namespace P2PMessenger.Spikes.Noise;

public static class Benchmark
{
    public const int DefaultHandshakes = 300;
    public const int DefaultMessages = 2000;

    private const int WarmUpRounds = 20;
    private const int MessageLength = 1024;

    public static int Run(int handshakes = DefaultHandshakes, int messages = DefaultMessages)
    {
        EnvironmentReport.Print();
        NoiseProtocol protocol = NoiseProtocol.KkBlake2b;
        PeerSetup setup = PeerSetup.CreateRandom();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{protocol.Name}: {handshakes} handshakes and {messages} transport messages of {MessageLength} bytes per candidate, after {WarmUpRounds} warm-up rounds"));
        Console.WriteLine("A handshake time covers both parties: creating them, importing the static keys, two messages and the split.");
        Console.WriteLine("A message time covers encrypting on one side and decrypting on the other.");
        Console.WriteLine();

        foreach (INoiseCandidate candidate in new INoiseCandidate[] { new OwnCandidate(), new NoiseNetCandidate() })
        {
            for (int round = 0; round < WarmUpRounds; round++)
            {
                RunHandshake(candidate, protocol, setup);
            }

            double[] times = new double[handshakes];
            for (int round = 0; round < handshakes; round++)
            {
                long start = Stopwatch.GetTimestamp();
                RunHandshake(candidate, protocol, setup);
                times[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            Array.Sort(times);
            double perMessage = MeasureTransport(candidate, protocol, setup, messages);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{candidate.Name,-12} handshake median {times[handshakes / 2]:F3} ms, 90th percentile {times[(int)(handshakes * 0.9)]:F3} ms; transport message {perMessage * 1000:F1} microseconds"));
        }

        return 0;
    }

    private static void RunHandshake(INoiseCandidate candidate, NoiseProtocol protocol, PeerSetup setup)
    {
        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        if (!Messages.Transfer(initiator, responder, default, out string problem)
            || !Messages.Transfer(responder, initiator, default, out problem))
        {
            throw new InvalidOperationException("The handshake failed: " + problem);
        }
    }

    private static double MeasureTransport(INoiseCandidate candidate, NoiseProtocol protocol, PeerSetup setup, int messages)
    {
        using INoiseParty initiator = setup.CreateInitiator(candidate, protocol);
        using INoiseParty responder = setup.CreateResponder(candidate, protocol);
        if (!Messages.Transfer(initiator, responder, default, out string problem)
            || !Messages.Transfer(responder, initiator, default, out problem))
        {
            throw new InvalidOperationException("The handshake failed: " + problem);
        }

        byte[] payload = RandomNumberGenerator.GetBytes(MessageLength);
        byte[] message = new byte[MessageLength + 16];
        byte[] received = new byte[MessageLength];
        for (int round = 0; round < WarmUpRounds; round++)
        {
            SendOne(initiator, responder, payload, message, received);
        }

        long start = Stopwatch.GetTimestamp();
        for (int round = 0; round < messages; round++)
        {
            SendOne(initiator, responder, payload, message, received);
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds / messages;
    }

    private static void SendOne(INoiseParty sender, INoiseParty receiver, byte[] payload, byte[] message, byte[] received)
    {
        int written = sender.WriteMessage(payload, message);
        if (!receiver.TryReadMessage(message.AsSpan(0, written), received, out int read) || read != payload.Length)
        {
            throw new InvalidOperationException("A transport message was refused.");
        }
    }
}
