using System.Globalization;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Cli;

internal sealed class SpikeLog : IDisposable
{
    private readonly TextWriter _console;
    private readonly StreamWriter _file;
    private readonly string _path;

    private SpikeLog(TextWriter console, StreamWriter file, string path)
    {
        _console = console;
        _file = file;
        _path = path;
    }

    public static string RunFolder => FindRunFolder();

    public static SpikeLog Start(string command)
    {
        string folder = FindRunFolder();
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, $"{command}-{stamp}.txt");
        StreamWriter file = new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        TextWriter console = Console.Out;
        Console.SetOut(TextWriter.Synchronized(new TeeWriter(console, file)));
        return new SpikeLog(console, file, path);
    }

    public void Finish(int exitCode)
    {
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Exit code {exitCode}. This output is also in {_path}"));
    }

    public void Dispose()
    {
        Console.Out.Flush();
        Console.SetOut(_console);
        _file.Dispose();
    }

    private static string FindRunFolder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "P2PMessenger.Spikes.Nearby.Cli")))
            {
                return Path.Combine(directory.FullName, "run");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, "run");
    }

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}
