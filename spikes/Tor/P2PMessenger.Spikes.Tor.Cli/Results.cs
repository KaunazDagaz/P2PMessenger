using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace P2PMessenger.Spikes.Tor.Cli;

internal sealed class Results
{
    private const string Header = "run_utc,os,tor,command,instance,metric,value_ms,detail";

    private readonly Lock _lock = new();
    private readonly List<Measurement> _measurements = [];
    private readonly string _runUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public void Add(string instance, string metric, TimeSpan value, string detail = "")
    {
        lock (_lock)
        {
            _measurements.Add(new Measurement(instance, metric, value.TotalMilliseconds, detail));
        }
    }

    public async Task SaveAsync(string stateDirectory, string command, string torVersion, CancellationToken cancellationToken)
    {
        var path = Path.Combine(stateDirectory, "results.csv");
        var builder = new StringBuilder();
        if (!File.Exists(path))
        {
            builder.AppendLine(Header);
        }

        foreach (var measurement in Snapshot())
        {
            builder.AppendLine(string.Join(
                ',',
                _runUtc,
                Quote(RuntimeInformation.OSDescription),
                Quote(torVersion),
                command,
                Quote(measurement.Instance),
                measurement.Metric,
                measurement.ValueMs.ToString("0.0", CultureInfo.InvariantCulture),
                Quote(measurement.Detail)));
        }

        await File.AppendAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Results appended to {path}");
    }

    public void PrintSummary()
    {
        var groups = Snapshot()
            .GroupBy(measurement => (measurement.Instance, measurement.Metric))
            .OrderBy(group => group.Key.Instance, StringComparer.Ordinal);

        Console.WriteLine();
        Console.WriteLine("| Instance | Metric | n | Median (s) | Min (s) | Max (s) |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var group in groups)
        {
            var values = group.Select(measurement => measurement.ValueMs / 1000).Order().ToArray();
            var median = values.Length % 2 == 1
                ? values[values.Length / 2]
                : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {group.Key.Instance} | {group.Key.Metric} | {values.Length} | {median:0.00} | {values[0]:0.00} | {values[^1]:0.00} |"));
        }

        Console.WriteLine();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private Measurement[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _measurements];
        }
    }

    private sealed record Measurement(string Instance, string Metric, double ValueMs, string Detail);
}
