using System.Security.Cryptography;
using NSec.Cryptography;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.E2E.Tests.Support;

internal sealed class DeviceKeyStore : IKeyProtector
{
    private const int NonceLength = 24;

    private readonly byte[] deviceKey = RandomNumberGenerator.GetBytes(32);

    public KeyProtection Protection => KeyProtection.WindowsDpapi;

    public Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        using Key key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
        byte[] wrapped = [.. nonce, .. AeadAlgorithm.XChaCha20Poly1305.Encrypt(key, nonce, [], dataKey.Span)];
        return Task.FromResult(ProtectResult.Success(wrapped));
    }

    public Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
    {
        using Key key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
        bool opened = AeadAlgorithm.XChaCha20Poly1305.Decrypt(key, protectedKey.Span[..NonceLength], [], protectedKey.Span[NonceLength..], dataKey.Span);
        return Task.FromResult(opened ? KeyStoreOutcome.Succeeded : KeyStoreOutcome.KeyNotFound);
    }

    public Task ForgetAsync(ReadOnlyMemory<byte> protectedKey, CancellationToken cancellationToken) => Task.CompletedTask;
}
