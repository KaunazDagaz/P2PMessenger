using System.Globalization;

namespace P2PMessenger.Spikes.AndroidHead.Cli;

internal sealed class CommandLine
{
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);

    private CommandLine(string command)
    {
        Command = command;
    }

    public string Command { get; }

    public static CommandLine Parse(string[] args)
    {
        var line = new CommandLine(args.Length > 0 ? args[0] : string.Empty);
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                line._positional.Add(argument);
                continue;
            }

            var name = argument[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                line._options[name] = args[++index];
            }
            else
            {
                line._options[name] = "true";
            }
        }

        return line;
    }

    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;

    public bool Flag(string name) => _options.ContainsKey(name);

    public string? Text(string name) => _options.GetValueOrDefault(name);

    public int Number(string name, int fallback) =>
        _options.TryGetValue(name, out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
