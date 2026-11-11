using System.Globalization;

namespace P2PMessenger.Spikes.AndroidHead.Probe;

public static class ProbeProtocol
{
    public const int DefaultPort = 45821;
    public const int MaxLineLength = 256;

    public static string FormatRequest(long sequence, long sentUnixMs) =>
        string.Create(CultureInfo.InvariantCulture, $"PING {sequence} {sentUnixMs}");

    public static string FormatReply(long sequence, long sentUnixMs, long receivedUnixMs, string state) =>
        string.Create(CultureInfo.InvariantCulture, $"PONG {sequence} {sentUnixMs} {receivedUnixMs} {state}");

    public static bool TryParseRequest(string line, out long sequence, out long sentUnixMs)
    {
        sequence = 0;
        sentUnixMs = 0;
        var parts = line.Split(' ');
        return parts.Length == 3
            && parts[0] == "PING"
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out sentUnixMs);
    }

    public static bool TryParseReply(string line, out long sequence, out long sentUnixMs, out long receivedUnixMs, out string state)
    {
        sequence = 0;
        sentUnixMs = 0;
        receivedUnixMs = 0;
        state = string.Empty;
        var parts = line.Split(' ', 5);
        if (parts.Length < 4 || parts[0] != "PONG")
        {
            return false;
        }

        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out sentUnixMs)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out receivedUnixMs))
        {
            return false;
        }

        state = parts.Length == 5 ? parts[4] : string.Empty;
        return true;
    }
}
