using System.Diagnostics;
using System.Globalization;

namespace P2PMessenger.Spikes.Nearby.Checks;

public sealed class CheckReport
{
    private readonly Dictionary<CheckStatus, int> _counts = new()
    {
        [CheckStatus.Pass] = 0,
        [CheckStatus.Fail] = 0,
        [CheckStatus.Skip] = 0,
        [CheckStatus.Info] = 0,
    };

    public int Failed => _counts[CheckStatus.Fail];

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title);
    }

    public void Run(string name, Func<CheckOutcome> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        long start = Stopwatch.GetTimestamp();
        CheckOutcome outcome;
        try
        {
            outcome = check();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            outcome = CheckOutcome.Fail($"{exception.GetType().Name}: {exception.Message}");
        }

        Record(name, outcome, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    public async Task RunAsync(string name, Func<Task<CheckOutcome>> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        long start = Stopwatch.GetTimestamp();
        CheckOutcome outcome;
        try
        {
            outcome = await check().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            outcome = CheckOutcome.Fail($"{exception.GetType().Name}: {exception.Message}");
        }

        Record(name, outcome, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{_counts[CheckStatus.Pass]} passed, {_counts[CheckStatus.Fail]} failed, {_counts[CheckStatus.Skip]} skipped, {_counts[CheckStatus.Info]} for information."));
    }

    private static string Label(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "PASS",
        CheckStatus.Fail => "FAIL",
        CheckStatus.Skip => "SKIP",
        _ => "INFO",
    };

    private void Record(string name, CheckOutcome outcome, double milliseconds)
    {
        _counts[outcome.Status]++;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Label(outcome.Status)}  {name}: {outcome.Detail} ({milliseconds:F0} ms)"));
    }
}
