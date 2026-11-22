using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.LocalData;

public readonly record struct LocalDataResult(LocalDataStatus Status, LocalDataStores? Stores, LocalDataProblem Problem)
{
    [MemberNotNullWhen(true, nameof(Stores))]
    public bool IsOpen => Stores is not null;

    public static LocalDataResult Opened(LocalDataStores stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        return new(LocalDataStatus.Opened, stores, LocalDataProblem.None);
    }

    public static LocalDataResult Failed(LocalDataProblem problem)
    {
        if (problem == LocalDataProblem.None || !Enum.IsDefined(problem))
        {
            throw new ArgumentOutOfRangeException(nameof(problem), problem, "A failure needs a problem.");
        }

        return new(LocalDataStatus.Failed, null, problem);
    }

    public static LocalDataResult Waiting(LocalDataStatus status)
    {
        if (status is LocalDataStatus.Opened or LocalDataStatus.Failed || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Opened and failed results carry stores or a problem.");
        }

        return new(status, null, LocalDataProblem.None);
    }
}
