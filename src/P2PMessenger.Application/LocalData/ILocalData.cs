namespace P2PMessenger.Application.LocalData;

public interface ILocalData : IAsyncDisposable
{
    Task<LocalDataResult> OpenAsync(CancellationToken cancellationToken);
}
