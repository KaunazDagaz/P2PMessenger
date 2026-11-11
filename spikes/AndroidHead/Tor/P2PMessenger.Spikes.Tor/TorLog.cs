namespace P2PMessenger.Spikes.Tor;

internal sealed class TorLog : IDisposable
{
    private const int TailLength = 40;

    private readonly Lock _lock = new();
    private readonly Queue<string> _tail = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public TorLog(string? path)
    {
        if (path is not null)
        {
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }
    }

    public void Add(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _writer?.WriteLine(line);
            _tail.Enqueue(line);
            while (_tail.Count > TailLength)
            {
                _tail.Dequeue();
            }
        }
    }

    public string Tail()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine, _tail);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
        }
    }
}
