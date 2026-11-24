namespace P2PMessenger.Transport.Tor;

internal interface ITorProcess : IDisposable
{
    bool HasExited { get; }

    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}
