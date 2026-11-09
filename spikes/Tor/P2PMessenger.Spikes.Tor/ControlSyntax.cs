using System.Text;

namespace P2PMessenger.Spikes.Tor;

internal static class ControlSyntax
{
    public static Dictionary<string, string> ParseKeywordArguments(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && text[index] == ' ')
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var keyStart = index;
            while (index < text.Length && text[index] != '=' && text[index] != ' ')
            {
                index++;
            }

            var key = text[keyStart..index];
            if (index >= text.Length || text[index] == ' ')
            {
                result[key] = string.Empty;
                continue;
            }

            index++;
            if (index < text.Length && text[index] == '"')
            {
                result[key] = ReadQuoted(text, ref index);
                continue;
            }

            var valueStart = index;
            while (index < text.Length && text[index] != ' ')
            {
                index++;
            }

            result[key] = text[valueStart..index];
        }

        return result;
    }

    public static IReadOnlyList<string> ParseQuotedList(string text)
    {
        var result = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == ' ')
            {
                index++;
                continue;
            }

            if (text[index] == '"')
            {
                result.Add(ReadQuoted(text, ref index));
                continue;
            }

            var start = index;
            while (index < text.Length && text[index] != ' ')
            {
                index++;
            }

            result.Add(text[start..index]);
        }

        return result;
    }

    public static string ReadQuoted(string text, ref int index)
    {
        if (index >= text.Length || text[index] != '"')
        {
            throw new FormatException("A quoted string must start with a double quote.");
        }

        index++;
        var bytes = new List<byte>();

        while (index < text.Length)
        {
            var current = text[index++];
            if (current == '"')
            {
                return Encoding.UTF8.GetString([.. bytes]);
            }

            if (current != '\\')
            {
                bytes.Add((byte)current);
                continue;
            }

            if (index >= text.Length)
            {
                break;
            }

            var escaped = text[index++];
            switch (escaped)
            {
                case 'n':
                    bytes.Add((byte)'\n');
                    break;
                case 'r':
                    bytes.Add((byte)'\r');
                    break;
                case 't':
                    bytes.Add((byte)'\t');
                    break;
                case >= '0' and <= '7':
                    var value = escaped - '0';
                    for (var digits = 1; digits < 3 && index < text.Length && text[index] is >= '0' and <= '7'; digits++)
                    {
                        value = (value * 8) + (text[index++] - '0');
                    }

                    bytes.Add((byte)value);
                    break;
                default:
                    bytes.Add((byte)escaped);
                    break;
            }
        }

        throw new FormatException("A quoted string has no closing double quote.");
    }
}
