using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace P2PMessenger.Transport.Tor;

internal static class ControlSyntax
{
    public static bool TryParseArguments(string text, [NotNullWhen(true)] out List<string>? positional, [NotNullWhen(true)] out Dictionary<string, string>? keywords)
    {
        ArgumentNullException.ThrowIfNull(text);
        positional = [];
        keywords = new Dictionary<string, string>(StringComparer.Ordinal);
        int index = 0;
        int count = 0;
        while (index < text.Length)
        {
            if (text[index] == ' ')
            {
                index++;
                continue;
            }

            if (++count > ControlLimits.MaxArguments)
            {
                positional = null;
                keywords = null;
                return false;
            }

            int tokenStart = index;
            while (index < text.Length && text[index] is not ' ' and not '=' and not '"')
            {
                index++;
            }

            if (index < text.Length && text[index] == '=' && index > tokenStart)
            {
                string key = text[tokenStart..index];
                index++;
                string value;
                if (index < text.Length && text[index] == '"')
                {
                    if (!TryReadQuoted(text, ref index, out string? quoted))
                    {
                        positional = null;
                        keywords = null;
                        return false;
                    }

                    value = quoted;
                }
                else
                {
                    int valueStart = index;
                    while (index < text.Length && text[index] != ' ')
                    {
                        index++;
                    }

                    value = text[valueStart..index];
                }

                keywords[key] = value;
                continue;
            }

            if (index < text.Length && text[index] == '"' && index == tokenStart)
            {
                if (!TryReadQuoted(text, ref index, out string? quoted))
                {
                    positional = null;
                    keywords = null;
                    return false;
                }

                positional.Add(quoted);
                continue;
            }

            while (index < text.Length && text[index] != ' ')
            {
                index++;
            }

            positional.Add(text[tokenStart..index]);
        }

        return true;
    }

    public static bool TryReadQuoted(string text, ref int index, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (index >= text.Length || text[index] != '"')
        {
            return false;
        }

        int position = index + 1;
        List<byte> bytes = [];
        while (position < text.Length)
        {
            char current = text[position++];
            if (current == '"')
            {
                value = Encoding.UTF8.GetString([.. bytes]);
                index = position;
                return true;
            }

            if (current != '\\')
            {
                bytes.Add((byte)current);
                continue;
            }

            if (position >= text.Length)
            {
                return false;
            }

            char escaped = text[position++];
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
                    int octal = escaped - '0';
                    for (int digits = 1; digits < 3 && position < text.Length && text[position] is >= '0' and <= '7'; digits++)
                    {
                        octal = (octal * 8) + (text[position++] - '0');
                    }

                    if (octal > byte.MaxValue)
                    {
                        return false;
                    }

                    bytes.Add((byte)octal);
                    break;
                default:
                    bytes.Add((byte)escaped);
                    break;
            }
        }

        return false;
    }
}
