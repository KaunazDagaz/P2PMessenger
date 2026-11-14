namespace P2PMessenger.Core.Tests.Noise;

internal sealed record NoiseVector(
    string Name,
    string Hash,
    string Prologue,
    string InitiatorStatic,
    string InitiatorEphemeral,
    string InitiatorRemoteStatic,
    string ResponderStatic,
    string ResponderEphemeral,
    string ResponderRemoteStatic,
    string? HandshakeHash,
    IReadOnlyList<NoiseVectorMessage> Messages);

internal sealed record NoiseVectorMessage(string Payload, string Ciphertext);
