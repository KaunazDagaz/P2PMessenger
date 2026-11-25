using P2PMessenger.Core;

namespace P2PMessenger.Tests.Shared;

internal sealed class RecordingFailureSink : IFailureSink
{
    private readonly Lock gate = new();
    private readonly List<(FailureSource Source, Exception Exception)> reports = [];

    public IReadOnlyList<(FailureSource Source, Exception Exception)> Reports
    {
        get
        {
            lock (gate)
            {
                return [.. reports];
            }
        }
    }

    public void Report(FailureSource source, Exception exception)
    {
        lock (gate)
        {
            reports.Add((source, exception));
        }
    }
}
