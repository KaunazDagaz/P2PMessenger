namespace P2PMessenger.Application.Background;

public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, bool> flags = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public bool ReadFlag(string key, bool fallback)
    {
        lock (gate)
        {
            return flags.TryGetValue(key, out bool value) ? value : fallback;
        }
    }

    public void WriteFlag(string key, bool value)
    {
        lock (gate)
        {
            flags[key] = value;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            flags.Clear();
        }
    }
}
