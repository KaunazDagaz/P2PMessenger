using System.Runtime.Versioning;
using P2PMessenger.Application.LocalData;
using Tmds.DBus.Protocol;

namespace P2PMessenger.Desktop.KeyStores;

[SupportedOSPlatform("linux")]
internal sealed class SecretServiceSession : IDisposable
{
    private const string Service = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string CollectionInterface = "org.freedesktop.Secret.Collection";
    private const string ItemInterface = "org.freedesktop.Secret.Item";
    private const string PromptInterface = "org.freedesktop.Secret.Prompt";
    private const string NoObject = "/";
    private const string DefaultAlias = "default";
    private const string DefaultCollectionLabel = "Login";
    private const string LabelProperty = "org.freedesktop.Secret.Item.Label";
    private const string AttributesProperty = "org.freedesktop.Secret.Item.Attributes";
    private const string CollectionLabelProperty = "org.freedesktop.Secret.Collection.Label";
    private const string SecretContentType = "application/octet-stream";

    private readonly DBusConnection connection;
    private readonly string sessionPath;
    private readonly TimeSpan promptTimeout;

    private SecretServiceSession(DBusConnection connection, string sessionPath, TimeSpan promptTimeout)
    {
        this.connection = connection;
        this.sessionPath = sessionPath;
        this.promptTimeout = promptTimeout;
    }

    public static async Task<SecretServiceSession> OpenAsync(string address, TimeSpan promptTimeout, CancellationToken cancellationToken)
    {
        DBusConnection connection = new(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            string session = await connection.CallMethodAsync(
                OpenSession(connection),
                static (Message message, object? state) =>
                {
                    Reader reader = message.GetBodyReader();
                    reader.ReadVariantValue();
                    return reader.ReadObjectPathAsString();
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SecretServiceSession(connection, session, promptTimeout);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<KeyStoreOutcome> StoreAsync(IReadOnlyDictionary<string, string> attributes, string label, byte[] secret, CancellationToken cancellationToken)
    {
        string? collection = await DefaultCollectionAsync(cancellationToken).ConfigureAwait(false);
        if (collection is null || await UnlockAsync(collection, cancellationToken).ConfigureAwait(false) != KeyStoreOutcome.Succeeded)
        {
            return KeyStoreOutcome.Refused;
        }

        (string item, string prompt) = await connection.CallMethodAsync(
            CreateItem(connection, collection, attributes, label, sessionPath, secret),
            static (Message message, object? state) =>
            {
                Reader reader = message.GetBodyReader();
                return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(item, NoObject, StringComparison.Ordinal)
            && (string.Equals(prompt, NoObject, StringComparison.Ordinal) || await RunPromptAsync(prompt, cancellationToken).ConfigureAwait(false)))
        {
            return KeyStoreOutcome.Refused;
        }

        return KeyStoreOutcome.Succeeded;
    }

    public async Task<(KeyStoreOutcome Outcome, byte[]? Secret)> FindAsync(IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
    {
        (string[] unlocked, string[] locked) = await connection.CallMethodAsync(
            SearchItems(connection, attributes),
            static (Message message, object? state) =>
            {
                Reader reader = message.GetBodyReader();
                return (Paths(reader.ReadArrayOfObjectPath()), Paths(reader.ReadArrayOfObjectPath()));
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

        string? item = unlocked.FirstOrDefault();
        if (item is null)
        {
            if (locked.Length == 0)
            {
                return (KeyStoreOutcome.KeyNotFound, null);
            }

            item = locked[0];
            KeyStoreOutcome unlockedNow = await UnlockAsync(item, cancellationToken).ConfigureAwait(false);
            if (unlockedNow != KeyStoreOutcome.Succeeded)
            {
                return (unlockedNow, null);
            }
        }

        byte[] secret = await connection.CallMethodAsync(
            GetSecret(connection, item, sessionPath),
            static (Message message, object? state) =>
            {
                Reader reader = message.GetBodyReader();
                reader.AlignStruct();
                reader.ReadObjectPathAsString();
                reader.ReadArrayOfByte();
                byte[] value = reader.ReadArrayOfByte();
                reader.ReadString();
                return value;
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

        return (KeyStoreOutcome.Succeeded, secret);
    }

    public void Dispose() => connection.Dispose();

    private static string[] Paths(ObjectPath[] paths) => [.. paths.Select(static path => path.ToString())];

    private static MessageBuffer OpenSession(DBusConnection connection)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ServicePath, @interface: ServiceInterface, member: "OpenSession", signature: "sv");
        writer.WriteString("plain");
        writer.WriteVariantString(string.Empty);
        return writer.CreateMessage();
    }

    private static MessageBuffer ReadAlias(DBusConnection connection)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ServicePath, @interface: ServiceInterface, member: "ReadAlias", signature: "s");
        writer.WriteString(DefaultAlias);
        return writer.CreateMessage();
    }

    private static MessageBuffer CreateCollection(DBusConnection connection)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ServicePath, @interface: ServiceInterface, member: "CreateCollection", signature: "a{sv}s");
        ArrayStart properties = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString(CollectionLabelProperty);
        writer.WriteVariantString(DefaultCollectionLabel);
        writer.WriteDictionaryEnd(properties);
        writer.WriteString(DefaultAlias);
        return writer.CreateMessage();
    }

    private static MessageBuffer Unlock(DBusConnection connection, string objectPath)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ServicePath, @interface: ServiceInterface, member: "Unlock", signature: "ao");
        ArrayStart objects = writer.WriteArrayStart(DBusType.ObjectPath);
        writer.WriteObjectPath(objectPath);
        writer.WriteArrayEnd(objects);
        return writer.CreateMessage();
    }

    private static MessageBuffer SearchItems(DBusConnection connection, IReadOnlyDictionary<string, string> attributes)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ServicePath, @interface: ServiceInterface, member: "SearchItems", signature: "a{ss}");
        ArrayStart entries = writer.WriteDictionaryStart();
        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(attribute.Key);
            writer.WriteString(attribute.Value);
        }

        writer.WriteDictionaryEnd(entries);
        return writer.CreateMessage();
    }

    private static MessageBuffer CreateItem(DBusConnection connection, string collection, IReadOnlyDictionary<string, string> attributes, string label, string session, byte[] secret)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: collection, @interface: CollectionInterface, member: "CreateItem", signature: "a{sv}(oayays)b");
        ArrayStart properties = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString(LabelProperty);
        writer.WriteVariantString(label);
        writer.WriteDictionaryEntryStart();
        writer.WriteString(AttributesProperty);
        writer.WriteSignature("a{ss}");
        ArrayStart attributeEntries = writer.WriteDictionaryStart();
        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(attribute.Key);
            writer.WriteString(attribute.Value);
        }

        writer.WriteDictionaryEnd(attributeEntries);
        writer.WriteDictionaryEnd(properties);
        writer.WriteStructureStart();
        writer.WriteObjectPath(session);
        writer.WriteArray(ReadOnlySpan<byte>.Empty);
        writer.WriteArray(secret);
        writer.WriteString(SecretContentType);
        writer.WriteBool(true);
        return writer.CreateMessage();
    }

    private static MessageBuffer GetSecret(DBusConnection connection, string item, string session)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: item, @interface: ItemInterface, member: "GetSecret", signature: "o");
        writer.WriteObjectPath(session);
        return writer.CreateMessage();
    }

    private static MessageBuffer Prompt(DBusConnection connection, string prompt)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: prompt, @interface: PromptInterface, member: "Prompt", signature: "s");
        writer.WriteString(string.Empty);
        return writer.CreateMessage();
    }

    private static MessageBuffer Dismiss(DBusConnection connection, string prompt)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: prompt, @interface: PromptInterface, member: "Dismiss");
        return writer.CreateMessage();
    }

    private async Task<string?> DefaultCollectionAsync(CancellationToken cancellationToken)
    {
        string collection = await ReadDefaultAliasAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(collection, NoObject, StringComparison.Ordinal))
        {
            return collection;
        }

        (string created, string prompt) = await connection.CallMethodAsync(
            CreateCollection(connection),
            static (Message message, object? state) =>
            {
                Reader reader = message.GetBodyReader();
                return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(created, NoObject, StringComparison.Ordinal))
        {
            return created;
        }

        if (string.Equals(prompt, NoObject, StringComparison.Ordinal) || await RunPromptAsync(prompt, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        collection = await ReadDefaultAliasAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(collection, NoObject, StringComparison.Ordinal) ? null : collection;
    }

    private Task<string> ReadDefaultAliasAsync(CancellationToken cancellationToken) =>
        connection.CallMethodAsync(
            ReadAlias(connection),
            static (Message message, object? state) => message.GetBodyReader().ReadObjectPathAsString()).WaitAsync(cancellationToken);

    private async Task<KeyStoreOutcome> UnlockAsync(string objectPath, CancellationToken cancellationToken)
    {
        (string[] unlocked, string prompt) = await connection.CallMethodAsync(
            Unlock(connection, objectPath),
            static (Message message, object? state) =>
            {
                Reader reader = message.GetBodyReader();
                return (Paths(reader.ReadArrayOfObjectPath()), reader.ReadObjectPathAsString());
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (unlocked.Contains(objectPath, StringComparer.Ordinal))
        {
            return KeyStoreOutcome.Succeeded;
        }

        if (string.Equals(prompt, NoObject, StringComparison.Ordinal))
        {
            return KeyStoreOutcome.Refused;
        }

        return await RunPromptAsync(prompt, cancellationToken).ConfigureAwait(false) ? KeyStoreOutcome.Refused : KeyStoreOutcome.Succeeded;
    }

    private async Task<bool> RunPromptAsync(string prompt, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable watch = await connection.WatchSignalAsync<bool>(
            sender: null,
            path: prompt,
            @interface: PromptInterface,
            signal: "Completed",
            reader: static (Message message, object? state) => message.GetBodyReader().ReadBool(),
            handler: (Notification<bool> notification) => completed.TrySetResult(!notification.HasValue || notification.Value),
            flags: ObserverFlags.None,
            emitOnCapturedContext: false).ConfigureAwait(false);

        await connection.CallMethodAsync(Prompt(connection, prompt)).WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await completed.Task.WaitAsync(promptTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await connection.CallMethodAsync(Dismiss(connection, prompt)).ConfigureAwait(false);
            return true;
        }
    }
}
