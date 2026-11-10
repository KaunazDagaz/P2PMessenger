using System.Text.Json;

namespace P2PMessenger.Spikes.Noise.Vectors;

public sealed record NoiseVector(
    string Source,
    string ProtocolName,
    ReadOnlyMemory<byte> InitiatorPrologue,
    ReadOnlyMemory<byte> InitiatorStatic,
    ReadOnlyMemory<byte> InitiatorEphemeral,
    ReadOnlyMemory<byte> InitiatorRemoteStatic,
    ReadOnlyMemory<byte> ResponderPrologue,
    ReadOnlyMemory<byte> ResponderStatic,
    ReadOnlyMemory<byte> ResponderEphemeral,
    ReadOnlyMemory<byte> ResponderRemoteStatic,
    ReadOnlyMemory<byte>? HandshakeHash,
    IReadOnlyList<NoiseVectorMessage> Messages)
{
    private const string ResourceName = "noise-kk-vectors.json";

    public static IReadOnlyList<NoiseVector> LoadEmbedded()
    {
        using Stream stream = typeof(NoiseVector).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing.");
        using JsonDocument document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(Parse)];
    }

    private static NoiseVector Parse(JsonElement vector)
    {
        return new NoiseVector(
            vector.GetProperty("source").GetString() ?? string.Empty,
            vector.GetProperty("protocol_name").GetString() ?? string.Empty,
            Hex(vector, "init_prologue"),
            Hex(vector, "init_static"),
            Hex(vector, "init_ephemeral"),
            Hex(vector, "init_remote_static"),
            Hex(vector, "resp_prologue"),
            Hex(vector, "resp_static"),
            Hex(vector, "resp_ephemeral"),
            Hex(vector, "resp_remote_static"),
            OptionalHex(vector, "handshake_hash"),
            [.. vector.GetProperty("messages").EnumerateArray().Select(message => new NoiseVectorMessage(Hex(message, "payload"), Hex(message, "ciphertext")))]);
    }

    private static byte[] Hex(JsonElement element, string property)
    {
        return Convert.FromHexString(element.GetProperty(property).GetString() ?? string.Empty);
    }

    private static ReadOnlyMemory<byte>? OptionalHex(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return Convert.FromHexString(value.GetString() ?? string.Empty);
    }
}
