using Android.Util;

namespace P2PMessenger.Spikes.AndroidHead.Head;

internal static class SpikeConsole
{
    private const string Tag = "AndroidHeadSpike";

    private static int s_installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref s_installed, 1) != 0)
        {
            return;
        }

        SpikeLog.Echo = line => _ = Log.Info(Tag, line);
        var writer = TextWriter.Synchronized(new LineWriter());
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    private sealed class LineWriter : TextWriter
    {
        private readonly System.Text.StringBuilder _line = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                SpikeLog.Write(_line.ToString());
                _line.Clear();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }
    }
}
