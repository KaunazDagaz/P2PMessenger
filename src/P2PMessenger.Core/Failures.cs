namespace P2PMessenger.Core;

public static class Failures
{
    public static bool IsRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is not OutOfMemoryException;
    }

    public static bool IsFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is not (OperationCanceledException or OutOfMemoryException);
    }

    public static string TypeName(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Type type = exception.GetType();
        return type.FullName ?? type.Name;
    }
}
