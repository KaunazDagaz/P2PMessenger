using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using P2PMessenger.Core;

namespace P2PMessenger.Application.Logging;

internal static class LogLine
{
    public const int MaxTextLength = 256;
    public const int MaxTemplateLength = 1024;

    private const string OriginalFormat = "{OriginalFormat}";
    private const string UnstructuredTemplate = "<unstructured log entry>";
    private const string UnknownField = "?";
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static byte[] Format<TState>(DateTimeOffset time, string category, LogLevel level, EventId eventId, TState state, Exception? exception)
    {
        string template = UnstructuredTemplate;
        List<Field> fields = [];
        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            foreach (KeyValuePair<string, object?> pair in pairs)
            {
                if (pair.Key == OriginalFormat)
                {
                    template = pair.Value is string text ? Truncated(text, MaxTemplateLength) : UnstructuredTemplate;
                }
                else
                {
                    fields.Add(Sanitize(pair.Key, pair.Value));
                }
            }
        }

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("time", time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
            json.WriteString("level", level.ToString());
            json.WriteString("category", Truncated(category, MaxTextLength));
            json.WriteNumber("eventId", eventId.Id);
            if (eventId.Name is { } name)
            {
                json.WriteString("event", Truncated(name, MaxTextLength));
            }

            json.WriteString("template", template);
            json.WriteString("message", Render(template, fields));
            json.WriteStartObject("fields");
            foreach (Field field in fields)
            {
                field.Write(json);
            }

            json.WriteEndObject();
            if (exception is not null)
            {
                json.WriteString("exception", Truncated(Failures.TypeName(exception), MaxTextLength));
            }

            json.WriteEndObject();
        }

        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    private static Field Sanitize(string name, object? value)
    {
        string key = Truncated(name, MaxTextLength);
        return value switch
        {
            null => new Field(key, null, "null"),
            bool flag => new Field(key, flag, flag ? "true" : "false"),
            Enum named => Text(key, named.ToString()),
            string text => Text(key, Truncated(text, MaxTextLength)),
            char character => Text(key, character.ToString()),
            sbyte or byte or short or ushort or int or uint or long => Integer(key, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ulong unsigned => new Field(key, unsigned, unsigned.ToString(CultureInfo.InvariantCulture)),
            float or double or decimal => Number(key, Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            TimeSpan span => Number(key, span.TotalMilliseconds),
            _ => Text(key, "<" + value.GetType().Name + ">"),
        };
    }

    private static Field Text(string key, string text) => new(key, text, text);

    private static Field Integer(string key, long number) => new(key, number, number.ToString(CultureInfo.InvariantCulture));

    private static Field Number(string key, double number) =>
        double.IsFinite(number)
            ? new Field(key, number, number.ToString("R", CultureInfo.InvariantCulture))
            : Text(key, number.ToString(CultureInfo.InvariantCulture));

    private static string Truncated(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        int length = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..length];
    }

    private static string Render(string template, List<Field> fields)
    {
        StringBuilder message = new(template.Length);
        for (int index = 0; index < template.Length; index++)
        {
            char current = template[index];
            bool doubled = index + 1 < template.Length && template[index + 1] == current;
            if ((current == '{' || current == '}') && doubled)
            {
                _ = message.Append(current);
                index++;
                continue;
            }

            int end = current == '{' ? template.IndexOf('}', index + 1) : -1;
            if (end < 0)
            {
                _ = message.Append(current);
                continue;
            }

            string placeholder = template[(index + 1)..end];
            int cut = placeholder.IndexOfAny([':', ',']);
            string name = (cut >= 0 ? placeholder[..cut] : placeholder).TrimStart('@', '$');
            _ = message.Append(fields.Find(field => field.Name == name)?.Text ?? UnknownField);
            index = end;
        }

        return message.ToString();
    }

    private sealed record Field(string Name, object? Value, string Text)
    {
        public void Write(Utf8JsonWriter json)
        {
            switch (Value)
            {
                case bool flag:
                    json.WriteBoolean(Name, flag);
                    break;
                case long number:
                    json.WriteNumber(Name, number);
                    break;
                case ulong unsigned:
                    json.WriteNumber(Name, unsigned);
                    break;
                case double real:
                    json.WriteNumber(Name, real);
                    break;
                case string text:
                    json.WriteString(Name, text);
                    break;
                default:
                    json.WriteNull(Name);
                    break;
            }
        }
    }
}
