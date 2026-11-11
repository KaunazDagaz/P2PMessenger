using System.Globalization;
using System.Text;

namespace P2PMessenger.Spikes.AndroidHead;

public static class SpikeLog
{
    private const int MaxLength = 400_000;

    private static readonly Lock Gate = new();
    private static readonly StringBuilder Text = new();

    public static event EventHandler<string>? LineWritten;

    public static Action<string>? Echo { get; set; }

    public static void Write(string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} {message}");
        lock (Gate)
        {
            Text.AppendLine(line);
            if (Text.Length > MaxLength)
            {
                Text.Remove(0, Text.Length - MaxLength);
            }
        }

        Echo?.Invoke(line);
        LineWritten?.Invoke(null, line);
    }

    public static string Snapshot()
    {
        lock (Gate)
        {
            return Text.ToString();
        }
    }

    public static string Tail(int characters)
    {
        lock (Gate)
        {
            return Text.Length <= characters ? Text.ToString() : Text.ToString(Text.Length - characters, characters);
        }
    }
}
