using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace P2PMessenger.Spikes.Nearby.Net;

public sealed record Observation(string Source, string Destination, string Mechanism, int Count, int FirstMs, int LastMs, string Detail)
{
    public string Key => MakeKey(Source, Destination, Mechanism);

    public static string MakeKey(string source, string destination, string mechanism) => $"{source}>{destination}|{mechanism}";

    public static bool TryDecode(string text, [NotNullWhen(true)] out Observation? observation)
    {
        observation = null;
        string[] parts = text.Split('|', 6);
        if (parts.Length != 6)
        {
            return false;
        }

        string[] ends = parts[0].Split('>');
        if (ends.Length != 2 || ends[0].Length == 0 || ends[1].Length == 0 || parts[1].Length == 0
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int first)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int last))
        {
            return false;
        }

        observation = new Observation(ends[0], ends[1], parts[1], count, first, last, parts[5]);
        return true;
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Source}>{Destination}|{Mechanism}|{Count}|{FirstMs}|{LastMs}|{Detail.Replace('|', '/')}");
}

public sealed class ObservationBook(string self)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Observation> _items = [];
    private readonly Dictionary<string, (int Last, int Longest)> _gaps = [];
    private int _cursor;

    public string Self { get; } = self;

    public void RecordHeard(string source, string mechanism, int nowMs, string detail, bool periodic = true)
    {
        lock (_gate)
        {
            string key = Observation.MakeKey(source, Self, mechanism);
            _items[key] = _items.TryGetValue(key, out Observation? existing)
                ? existing with { Count = existing.Count + 1, LastMs = nowMs }
                : new Observation(source, Self, mechanism, 1, nowMs, nowMs, detail);
            if (periodic)
            {
                _gaps[key] = _gaps.TryGetValue(key, out (int Last, int Longest) gap)
                    ? (nowMs, Math.Max(gap.Longest, nowMs - gap.Last))
                    : (nowMs, 0);
            }
        }
    }

    public IReadOnlyList<string> Silences(int nowMs, int thresholdMs)
    {
        lock (_gate)
        {
            List<string> lines = [];
            foreach ((string key, (int last, int longest)) in _gaps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                int trailing = nowMs - last;
                if (longest >= thresholdMs || trailing >= thresholdMs)
                {
                    lines.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {key}: longest silence between two datagrams {longest / 1000.0:F0} s, silent for the last {trailing / 1000.0:F0} s"));
                }
            }

            return lines;
        }
    }

    public void Set(Observation observation)
    {
        lock (_gate)
        {
            _items[observation.Key] = observation;
        }
    }

    public void RecordServed(string source, int nowMs, string detail)
    {
        lock (_gate)
        {
            string key = Observation.MakeKey(source, Self, "tcp");
            _items.TryAdd(key, new Observation(source, Self, "tcp", 1, nowMs, nowMs, detail));
        }
    }

    public int Merge(IEnumerable<string> entries)
    {
        int accepted = 0;
        lock (_gate)
        {
            foreach (string text in entries)
            {
                if (!Observation.TryDecode(text, out Observation? incoming) || OwnedByMe(incoming))
                {
                    continue;
                }

                if (!_items.TryGetValue(incoming.Key, out Observation? existing) || incoming.Count >= existing.Count)
                {
                    _items[incoming.Key] = incoming;
                    accepted++;
                }
            }
        }

        return accepted;
    }

    public IReadOnlyList<string> NextGossip(int maximum)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return [];
            }

            string[] keys = [.. _items.Keys.Order(StringComparer.Ordinal)];
            List<string> entries = [];
            int take = Math.Min(maximum, keys.Length);
            for (int i = 0; i < take; i++)
            {
                entries.Add(_items[keys[(_cursor + i) % keys.Length]].Encode());
            }

            _cursor = (_cursor + take) % keys.Length;
            return entries;
        }
    }

    public IReadOnlyList<Observation> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items.Values];
        }
    }

    private bool OwnedByMe(Observation observation) =>
        observation.Mechanism == "tcp" ? observation.Source == Self : observation.Destination == Self;
}
