using System.Text;
using Log = Android.Util.Log;

namespace P2PMessenger.Spikes.Nearby.Android;

internal static class SpikeConsole
{
    private const string Tag = "NearbySpike";
    private const int MaxLength = 400_000;

    private static readonly Lock Gate = new();
    private static readonly StringBuilder Text = new();
    private static int s_installed;

    public static event EventHandler<string>? LineWritten;

    public static void Install()
    {
        if (Interlocked.Exchange(ref s_installed, 1) != 0)
        {
            return;
        }

        var writer = TextWriter.Synchronized(new LineWriter());
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    public static string Snapshot()
    {
        lock (Gate)
        {
            return Text.ToString();
        }
    }

    private static void OnLine(string line)
    {
        lock (Gate)
        {
            Text.AppendLine(line);
            if (Text.Length > MaxLength)
            {
                Text.Remove(0, Text.Length - MaxLength);
            }
        }

        _ = Log.Info(Tag, line);
        LineWritten?.Invoke(null, line);
    }

    private sealed class LineWriter : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                OnLine(_line.ToString());
                _line.Clear();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }
    }
}
