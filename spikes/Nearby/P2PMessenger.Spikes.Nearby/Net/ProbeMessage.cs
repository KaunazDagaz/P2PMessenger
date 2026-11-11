using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record ProbeMessage(string Node, string RunId, string Mechanism, int Sequence, int TcpPort, IReadOnlyList<string> Knowledge)
{
    public const string Signature = "p2pm-s05";
    public const int MaxLength = 1400;
    public const int MaxKnowledgeEntries = 24;

    private const int MaxNameLength = 40;
    private const int MaxEntryLength = 120;

    public byte[] ToBytes()
    {
        ArrayBufferWriter<byte> buffer = new(512);
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("s", Signature);
            writer.WriteNumber("v", 1);
            writer.WriteString("run", RunId);
            writer.WriteString("node", Node);
            writer.WriteString("mech", Mechanism);
            writer.WriteNumber("seq", Sequence);
            writer.WriteNumber("tcp", TcpPort);
            writer.WriteStartArray("k");
            foreach (string entry in Knowledge)
            {
                writer.WriteStringValue(entry);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out ProbeMessage? message)
    {
        message = null;
        if (data.Length is < 20 or > MaxLength * 2 || data[0] != (byte)'{')
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("s", out JsonElement signature) || signature.GetString() != Signature
                || !root.TryGetProperty("run", out JsonElement run) || run.GetString() is not { Length: > 0 and <= 16 } runId
                || !root.TryGetProperty("node", out JsonElement node) || node.GetString() is not { Length: > 0 } nodeName || nodeName.Length > MaxNameLength
                || !root.TryGetProperty("mech", out JsonElement mechanism) || mechanism.GetString() is not { Length: > 0 and <= 24 } mechanismName
                || !root.TryGetProperty("seq", out JsonElement sequence) || !sequence.TryGetInt32(out int sequenceNumber)
                || !root.TryGetProperty("tcp", out JsonElement tcp) || !tcp.TryGetInt32(out int tcpPort) || tcpPort is < 0 or > ushort.MaxValue
                || !root.TryGetProperty("k", out JsonElement knowledge) || knowledge.ValueKind != JsonValueKind.Array
                || knowledge.GetArrayLength() > MaxKnowledgeEntries)
            {
                return false;
            }

            if (!IsPlain(nodeName) || !IsPlain(mechanismName))
            {
                return false;
            }

            List<string> entries = [];
            foreach (JsonElement entry in knowledge.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { Length: > 0 } text || text.Length > MaxEntryLength)
                {
                    return false;
                }

                entries.Add(text);
            }

            message = new ProbeMessage(nodeName, runId, mechanismName, sequenceNumber, tcpPort, entries);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string SanitizeName(string name)
    {
        StringBuilder builder = new();
        foreach (char c in name.Trim())
        {
            if (builder.Length >= MaxNameLength)
            {
                break;
            }

            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        }

        return builder.Length == 0 ? "node" : builder.ToString();
    }

    private static bool IsPlain(string text) => text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
