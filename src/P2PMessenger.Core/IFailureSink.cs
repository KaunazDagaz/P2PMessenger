namespace P2PMessenger.Core;

public interface IFailureSink
{
    void Report(FailureSource source, Exception exception);
}
