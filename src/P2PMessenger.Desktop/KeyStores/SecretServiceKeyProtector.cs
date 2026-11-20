using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using NSec.Cryptography;
using P2PMessenger.Application.LocalData;
using Tmds.DBus.Protocol;

namespace P2PMessenger.Desktop.KeyStores;

[SupportedOSPlatform("linux")]
public sealed class SecretServiceKeyProtector : IKeyProtector
{
    public const int KeyIdLength = 16;
    public const int ProtectedKeyLength = 1 + KeyIdLength + NonceLength + DataKeyLength + TagLength;

    private const byte FormatVersion = 1;
    private const int DataKeyLength = 32;
    private const int WrappingKeyLength = 32;
    private const int NonceLength = 24;
    private const int TagLength = 16;
    private const int NonceOffset = 1 + KeyIdLength;
    private const int CiphertextOffset = NonceOffset + NonceLength;
    private const string SchemaAttribute = "xdg:schema";
    private const string SchemaName = "P2PMessenger.DataKey";
    private const string KeyIdAttribute = "key-id";
    private const string ItemLabel = "P2PMessenger data key";

    private readonly string? busAddress;
    private readonly TimeSpan promptTimeout;

    public SecretServiceKeyProtector()
        : this(DBusAddress.Session, TimeSpan.FromMinutes(2))
    {
    }

    public SecretServiceKeyProtector(string? busAddress, TimeSpan promptTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(promptTimeout, TimeSpan.Zero);

        this.busAddress = busAddress;
        this.promptTimeout = promptTimeout;
    }

    public KeyProtection Protection => KeyProtection.LinuxSecretService;

    private static ReadOnlySpan<byte> Context => "P2PM/data-key/secret-service/v1"u8;

    public async Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
    {
        if (dataKey.Length != DataKeyLength)
        {
            throw new ArgumentException("The data key must be 32 bytes long.", nameof(dataKey));
        }

        if (string.IsNullOrEmpty(busAddress))
        {
            return ProtectResult.Failure(KeyStoreOutcome.Unavailable);
        }

        byte[] keyId = RandomNumberGenerator.GetBytes(KeyIdLength);
        byte[] wrappingKey = RandomNumberGenerator.GetBytes(WrappingKeyLength);
        try
        {
            KeyStoreOutcome stored;
            try
            {
                using SecretServiceSession session = await SecretServiceSession.OpenAsync(busAddress, promptTimeout, cancellationToken).ConfigureAwait(false);
                stored = await session.StoreAsync(Attributes(keyId), ItemLabel, wrappingKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsKeyStoreFailure(exception))
            {
                stored = OutcomeOf(exception);
            }

            return stored == KeyStoreOutcome.Succeeded
                ? ProtectResult.Success(Wrap(keyId, wrappingKey, dataKey.Span))
                : ProtectResult.Failure(stored);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public async Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
    {
        if (dataKey.Length != DataKeyLength)
        {
            throw new ArgumentException("The data key buffer must be 32 bytes long.", nameof(dataKey));
        }

        if (protectedKey.Length != ProtectedKeyLength || protectedKey.Span[0] != FormatVersion)
        {
            return KeyStoreOutcome.KeyNotFound;
        }

        if (string.IsNullOrEmpty(busAddress))
        {
            return KeyStoreOutcome.Unavailable;
        }

        byte[] keyId = protectedKey.Span.Slice(1, KeyIdLength).ToArray();
        KeyStoreOutcome found;
        byte[]? wrappingKey;
        try
        {
            using SecretServiceSession session = await SecretServiceSession.OpenAsync(busAddress, promptTimeout, cancellationToken).ConfigureAwait(false);
            (found, wrappingKey) = await session.FindAsync(Attributes(keyId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsKeyStoreFailure(exception))
        {
            return OutcomeOf(exception);
        }

        if (wrappingKey is null)
        {
            return found;
        }

        try
        {
            return Unwrap(protectedKey.Span, keyId, wrappingKey, dataKey.Span) ? KeyStoreOutcome.Succeeded : KeyStoreOutcome.KeyNotFound;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static Dictionary<string, string> Attributes(byte[] keyId) => new(StringComparer.Ordinal)
    {
        [SchemaAttribute] = SchemaName,
        [KeyIdAttribute] = Convert.ToHexStringLower(keyId),
    };

    private static byte[] AssociatedData(byte[] keyId) => [.. Context, FormatVersion, .. keyId];

    private static byte[] Wrap(byte[] keyId, byte[] wrappingKey, ReadOnlySpan<byte> dataKey)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);

        byte[] wrapped = new byte[ProtectedKeyLength];
        wrapped[0] = FormatVersion;
        keyId.CopyTo(wrapped.AsSpan(1));
        nonce.CopyTo(wrapped.AsSpan(NonceOffset));

        using Key key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, wrappingKey, KeyBlobFormat.RawSymmetricKey);
        AeadAlgorithm.XChaCha20Poly1305.Encrypt(key, nonce, AssociatedData(keyId), dataKey, wrapped.AsSpan(CiphertextOffset));
        return wrapped;
    }

    private static bool Unwrap(ReadOnlySpan<byte> wrapped, byte[] keyId, byte[] wrappingKey, Span<byte> dataKey)
    {
        if (wrappingKey.Length != WrappingKeyLength)
        {
            return false;
        }

        using Key key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, wrappingKey, KeyBlobFormat.RawSymmetricKey);
        return AeadAlgorithm.XChaCha20Poly1305.Decrypt(key, wrapped.Slice(NonceOffset, NonceLength), AssociatedData(keyId), wrapped[CiphertextOffset..], dataKey);
    }

    private static bool IsKeyStoreFailure(Exception exception) =>
        exception is DBusExceptionBase or SocketException or IOException;

    private static KeyStoreOutcome OutcomeOf(Exception exception) => exception switch
    {
        DBusErrorReplyException reply when IsServiceMissing(reply.ErrorName) => KeyStoreOutcome.Unavailable,
        DBusErrorReplyException => KeyStoreOutcome.Refused,
        _ => KeyStoreOutcome.Unavailable,
    };

    private static bool IsServiceMissing(string errorName) =>
        errorName is "org.freedesktop.DBus.Error.ServiceUnknown"
            or "org.freedesktop.DBus.Error.NameHasNoOwner"
            or "org.freedesktop.DBus.Error.Spawn.ChildExited"
            or "org.freedesktop.DBus.Error.Spawn.ExecFailed"
            or "org.freedesktop.DBus.Error.Spawn.ServiceNotFound"
            or "org.freedesktop.DBus.Error.UnknownObject"
            or "org.freedesktop.DBus.Error.UnknownMethod";
}
