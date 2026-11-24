using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace P2PMessenger.Transport.Tor;

internal static class TorEvents
{
    public const int ServiceIdLength = 56;

    public static IReadOnlyList<string> Subscribed { get; } = ["STATUS_CLIENT", "STATUS_GENERAL", "HS_DESC", "NETWORK_LIVENESS"];

    public static bool TryParse(string text, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = null;
        if (!ControlSyntax.TryParseArguments(text, out List<string>? positional, out Dictionary<string, string>? keywords) || positional.Count == 0)
        {
            return false;
        }

        switch (positional[0])
        {
            case "STATUS_CLIENT" when positional.Count >= 3:
                return TryParseClient(positional, keywords, out parsed);
            case "STATUS_GENERAL" when positional.Count >= 3:
                return TryParseGeneral(positional[2], out parsed);
            case "HS_DESC" when positional.Count >= 3:
                return TryParseDescriptor(positional[1], positional[2], out parsed);
            case "NETWORK_LIVENESS" when positional.Count >= 2:
                return TryParseLiveness(positional[1], out parsed);
            default:
                return false;
        }
    }

    public static bool TryParseBootstrapPhase(string text, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = null;
        return ControlSyntax.TryParseArguments(text, out List<string>? positional, out Dictionary<string, string>? keywords)
            && positional.Count >= 2
            && positional[1] == "BOOTSTRAP"
            && TryParseBootstrap(positional[0], keywords, out parsed);
    }

    public static bool IsServiceId(string text) =>
        text.Length == ServiceIdLength && text.All(static character => character is (>= 'a' and <= 'z') or (>= '2' and <= '7'));

    private static bool TryParseClient(List<string> positional, Dictionary<string, string> keywords, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = null;
        switch (positional[2])
        {
            case "BOOTSTRAP":
                return TryParseBootstrap(positional[1], keywords, out parsed);
            case "CIRCUIT_ESTABLISHED":
                parsed = new TorEvent(TorEventKind.CircuitsEstablished);
                return true;
            case "CIRCUIT_NOT_ESTABLISHED":
                parsed = new TorEvent(TorEventKind.CircuitsNotEstablished, Reason: KnownReason(keywords.GetValueOrDefault("REASON")));
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseBootstrap(string severity, Dictionary<string, string> keywords, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = null;
        if (!keywords.TryGetValue("PROGRESS", out string? progressText)
            || !int.TryParse(progressText, NumberStyles.None, CultureInfo.InvariantCulture, out int progress)
            || progress > 100)
        {
            return false;
        }

        bool warning = severity is "WARN" or "ERR";
        parsed = new TorEvent(TorEventKind.Bootstrap, progress, warning, warning ? KnownReason(keywords.GetValueOrDefault("REASON")) : null);
        return true;
    }

    private static bool TryParseGeneral(string action, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = action switch
        {
            "CLOCK_SKEW" => new TorEvent(TorEventKind.ClockSkew),
            "CLOCK_JUMPED" => new TorEvent(TorEventKind.ClockJumped),
            "DIR_ALL_UNREACHABLE" => new TorEvent(TorEventKind.DirectoriesUnreachable),
            _ => null,
        };
        return parsed is not null;
    }

    private static bool TryParseDescriptor(string action, string serviceId, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = null;
        if (!IsServiceId(serviceId))
        {
            return false;
        }

        parsed = action switch
        {
            "UPLOADED" => new TorEvent(TorEventKind.DescriptorUploaded, ServiceId: serviceId),
            "FAILED" => new TorEvent(TorEventKind.DescriptorFailed, ServiceId: serviceId),
            _ => null,
        };
        return parsed is not null;
    }

    private static bool TryParseLiveness(string state, [NotNullWhen(true)] out TorEvent? parsed)
    {
        parsed = state switch
        {
            "UP" => new TorEvent(TorEventKind.NetworkUp),
            "DOWN" => new TorEvent(TorEventKind.NetworkDown),
            _ => null,
        };
        return parsed is not null;
    }

    private static string? KnownReason(string? reason) =>
        reason is { Length: > 0 and <= 32 } && reason.All(static character => character is (>= 'A' and <= 'Z') or '_')
            ? reason
            : null;
}
