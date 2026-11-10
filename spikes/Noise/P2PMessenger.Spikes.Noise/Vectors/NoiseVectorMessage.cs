namespace P2PMessenger.Spikes.Noise.Vectors;

public sealed record NoiseVectorMessage(ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> Ciphertext);
