using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Application.LocalData;

public readonly record struct LocalDataResult(LocalDataStores? Stores, LocalDataProblem Problem)
{
    [MemberNotNullWhen(true, nameof(Stores))]
    public bool IsOpen => Stores is not null;

    internal static LocalDataResult Opened(LocalDataStores stores) => new(stores, LocalDataProblem.None);

    internal static LocalDataResult Failed(LocalDataProblem problem) => new(null, problem);
}
