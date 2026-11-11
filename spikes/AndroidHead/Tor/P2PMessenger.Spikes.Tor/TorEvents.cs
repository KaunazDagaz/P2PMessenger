using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace P2PMessenger.Spikes.Tor;

public sealed record BootstrapPhase(int Progress, string Tag, string Summary, string? Warning);

public sealed record HiddenServiceDescriptorEvent(string Action, string Address, string? Reason);

public static class TorEvents
{
    private const string BootstrapKeyword = "BOOTSTRAP ";
    private const string HsDescPrefix = "HS_DESC ";

    public static bool TryParseBootstrapPhase(string text, [NotNullWhen(true)] out BootstrapPhase? phase)
    {
        ArgumentNullException.ThrowIfNull(text);
        phase = null;

        var start = text.IndexOf(BootstrapKeyword, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var arguments = ControlSyntax.ParseKeywordArguments(text[(start + BootstrapKeyword.Length)..]);
        if (!arguments.TryGetValue("PROGRESS", out var progressText)
            || !int.TryParse(progressText, NumberStyles.None, CultureInfo.InvariantCulture, out var progress))
        {
            return false;
        }

        arguments.TryGetValue("WARNING", out var warning);
        phase = new BootstrapPhase(
            progress,
            arguments.GetValueOrDefault("TAG", string.Empty),
            arguments.GetValueOrDefault("SUMMARY", string.Empty),
            warning);
        return true;
    }

    public static bool TryParseHiddenServiceDescriptor(string text, [NotNullWhen(true)] out HiddenServiceDescriptorEvent? descriptorEvent)
    {
        ArgumentNullException.ThrowIfNull(text);
        descriptorEvent = null;

        if (!text.StartsWith(HsDescPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = text[HsDescPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return false;
        }

        string? reason = null;
        foreach (var field in fields)
        {
            if (field.StartsWith("REASON=", StringComparison.Ordinal))
            {
                reason = field["REASON=".Length..];
            }
        }

        descriptorEvent = new HiddenServiceDescriptorEvent(fields[0], fields[1], reason);
        return true;
    }
}
