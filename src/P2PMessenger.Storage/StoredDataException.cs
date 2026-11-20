namespace P2PMessenger.Storage;

public sealed class StoredDataException : Exception
{
    public StoredDataException()
        : base("A stored value is damaged or was changed.")
    {
    }

    public StoredDataException(string message)
        : base(message)
    {
    }

    public StoredDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
