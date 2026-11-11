using System.Globalization;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Net;

public static class NodeReport
{
    public static IReadOnlyList<string> Render(string self, IReadOnlyList<Observation> observations, IReadOnlyCollection<string> known, IReadOnlyCollection<string> informed, IReadOnlyList<string> columns)
    {
        List<string> names = [self];
        foreach (string name in observations.SelectMany(item => new[] { item.Source, item.Destination }).Concat(known).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (name != self && !name.StartsWith('?'))
            {
                names.Add(name);
            }
        }

        if (names.Count == 1)
        {
            return
            [
                "No other node was heard by any mechanism. Check that the node runs on the other devices with the same UDP port, that the devices share one network,",
                "that no firewall blocks UDP and TCP on these ports, and that the hotspot does not isolate its clients.",
            ];
        }

        Dictionary<string, Observation> byKey = observations.ToDictionary(item => item.Key, StringComparer.Ordinal);
        List<(string Source, string Destination)> pairs = [];
        foreach (string source in names)
        {
            foreach (string destination in names)
            {
                if (source != destination)
                {
                    pairs.Add((source, destination));
                }
            }
        }

        List<string[]> rows = [];
        foreach ((string source, string destination) in pairs)
        {
            string[] row = new string[columns.Count + 1];
            row[0] = source + " -> " + destination;
            for (int i = 0; i < columns.Count; i++)
            {
                row[i + 1] = Cell(source, destination, columns[i], self, byKey, informed);
            }

            rows.Add(row);
        }

        List<string> lines = [];
        string[] header = ["from -> to", .. columns];
        lines.AddRange(Table(header, rows));
        lines.Add(string.Empty);
        lines.AddRange(Verdicts(self, pairs, columns, byKey, informed));
        return lines;
    }

    private static string Cell(string source, string destination, string mechanism, string self, Dictionary<string, Observation> byKey, IReadOnlyCollection<string> informed)
    {
        if (byKey.TryGetValue(Observation.MakeKey(source, destination, mechanism), out Observation? item))
        {
            if (mechanism == "tcp")
            {
                return item.Count > 0 ? "ok " + item.Detail : "FAIL " + item.Detail;
            }

            return item.Count.ToString(CultureInfo.InvariantCulture);
        }

        string observer = mechanism == "tcp" ? source : destination;
        return observer == self || informed.Contains(observer) ? "-" : "?";
    }

    private static IEnumerable<string> Verdicts(string self, List<(string Source, string Destination)> pairs, IReadOnlyList<string> columns, Dictionary<string, Observation> byKey, IReadOnlyCollection<string> informed)
    {
        yield return "Verdict per mechanism (a pair counts as working when at least one datagram or one TCP test went through):";
        foreach (string mechanism in columns)
        {
            List<string> missing = [];
            List<string> unknown = [];
            int working = 0;
            foreach ((string source, string destination) in pairs)
            {
                if (byKey.TryGetValue(Observation.MakeKey(source, destination, mechanism), out Observation? item) && item.Count > 0)
                {
                    working++;
                    continue;
                }

                string observer = mechanism == "tcp" ? source : destination;
                (observer == self || informed.Contains(observer) ? missing : unknown).Add(source + "->" + destination);
            }

            StringBuilder line = new();
            line.Append(CultureInfo.InvariantCulture, $"  {mechanism,-9} {working} of {pairs.Count} directions work");
            if (missing.Count > 0)
            {
                line.Append("; not working: ").Append(string.Join(", ", missing));
            }

            if (unknown.Count > 0)
            {
                line.Append("; no report yet: ").Append(string.Join(", ", unknown));
            }

            yield return line.ToString();
        }

        List<string> names = [.. pairs.Select(pair => pair.Source).Distinct(StringComparer.Ordinal)];
        for (int i = 0; i < names.Count; i++)
        {
            for (int j = i + 1; j < names.Count; j++)
            {
                bool anyPath = columns.Any(mechanism =>
                    (byKey.TryGetValue(Observation.MakeKey(names[i], names[j], mechanism), out Observation? forward) && forward.Count > 0)
                    || (byKey.TryGetValue(Observation.MakeKey(names[j], names[i], mechanism), out Observation? backward) && backward.Count > 0));
                bool bothInformed = (names[i] == self || informed.Contains(names[i])) && (names[j] == self || informed.Contains(names[j]));
                if (!anyPath && bothInformed)
                {
                    yield return $"  NO PATH between {names[i]} and {names[j]} by any mechanism: client isolation on the hotspot or a firewall is likely";
                }
            }
        }
    }

    private static List<string> Table(string[] header, List<string[]> rows)
    {
        int[] widths = new int[header.Length];
        for (int column = 0; column < header.Length; column++)
        {
            widths[column] = Math.Max(header[column].Length, rows.Count == 0 ? 0 : rows.Max(row => row[column].Length));
        }

        List<string> lines = [FormatRow(header, widths), FormatRow([.. widths.Select(width => new string('-', width))], widths)];
        lines.AddRange(rows.Select(row => FormatRow(row, widths)));
        return lines;
    }

    private static string FormatRow(string[] cells, int[] widths)
    {
        StringBuilder line = new("  ");
        for (int column = 0; column < cells.Length; column++)
        {
            line.Append(cells[column].PadRight(widths[column])).Append("  ");
        }

        return line.ToString().TrimEnd();
    }
}
