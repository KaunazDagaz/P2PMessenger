using P2PMessenger.Application.LocalData;

namespace P2PMessenger.UI.Tests;

internal sealed class FakeLocalData : ILocalData
{
    private readonly InMemoryLocalData memory;

    public FakeLocalData(LocalDataProblem problem, LocalDataMode mode = LocalDataMode.MemoryOnly)
    {
        Problem = problem;
        memory = new InMemoryLocalData(mode);
    }

    public LocalDataProblem Problem { get; set; }

    public int Opened { get; private set; }

    public async Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken)
    {
        Opened++;
        return Problem == LocalDataProblem.None
            ? await memory.OpenAsync(cancellationToken)
            : new LocalDataResult(null, Problem);
    }

    public ValueTask DisposeAsync() => memory.DisposeAsync();
}
