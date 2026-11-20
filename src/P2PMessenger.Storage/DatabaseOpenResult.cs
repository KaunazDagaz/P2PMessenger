using System.Diagnostics.CodeAnalysis;

namespace P2PMessenger.Storage;

public readonly record struct DatabaseOpenResult(MessengerDatabase? Database, DatabaseOpenOutcome Outcome)
{
    [MemberNotNullWhen(true, nameof(Database))]
    public bool IsOpened => Database is not null;
}
