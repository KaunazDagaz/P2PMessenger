using P2PMessenger.Application.Background;

namespace P2PMessenger.Application.Tests.Background;

internal sealed class MemorySettings : ISettingsStore
{
    private readonly Dictionary<string, bool> flags = new(StringComparer.Ordinal);

    public int Count => flags.Count;

    public bool ReadFlag(string key, bool fallback) => flags.TryGetValue(key, out bool value) ? value : fallback;

    public void WriteFlag(string key, bool value) => flags[key] = value;

    public void Clear() => flags.Clear();
}
