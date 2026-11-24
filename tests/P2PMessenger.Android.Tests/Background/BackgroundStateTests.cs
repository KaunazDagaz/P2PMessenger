using P2PMessenger.Android.Background;
using P2PMessenger.Application.Background;

namespace P2PMessenger.Android.Tests.Background;

public sealed class BackgroundStateTests
{
    [Fact]
    [Trait("Story", "US-18")]
    public void IsWanted_ByDefault_IsOffAndIsStored()
    {
        var flags = new Flags();
        var state = new BackgroundState(flags);

        Assert.False(state.IsWanted);
        state.IsWanted = true;

        Assert.True(new BackgroundState(flags).IsWanted);
        Assert.True(flags.ReadFlag(BackgroundState.WantedKey, false));
    }

    private sealed class Flags : ISettingsStore
    {
        private readonly Dictionary<string, bool> values = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => values.Keys;

        public bool ReadFlag(string key, bool fallback) => values.TryGetValue(key, out bool value) ? value : fallback;

        public void WriteFlag(string key, bool value) => values[key] = value;

        public void Clear() => values.Clear();
    }
}
