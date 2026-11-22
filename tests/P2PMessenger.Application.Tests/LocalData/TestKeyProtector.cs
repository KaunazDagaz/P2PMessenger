using System.Security.Cryptography;
using NSec.Cryptography;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Application.Tests.LocalData;

internal sealed class TestKeyProtector : IKeyProtector
{
    private const int NonceLength = 24;

    private readonly byte[] deviceKey = RandomNumberGenerator.GetBytes(32);

    public KeyProtection Protection { get; init; } = KeyProtection.WindowsDpapi;

    public KeyStoreOutcome Outcome { get; set; } = KeyStoreOutcome.Succeeded;

    public int ProtectCalls { get; private set; }

    public int UnprotectCalls { get; private set; }

    public List<byte[]> Forgotten { get; } = [];

    public Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
    {
        ProtectCalls++;
        if (Outcome != KeyStoreOutcome.Succeeded)
        {
            return Task.FromResult(ProtectResult.Failure(Outcome));
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        using var key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
        byte[] wrapped = [.. nonce, .. AeadAlgorithm.XChaCha20Poly1305.Encrypt(key, nonce, [], dataKey.Span)];
        return Task.FromResult(ProtectResult.Success(wrapped));
    }

    public Task ForgetAsync(ReadOnlyMemory<byte> protectedKey, CancellationToken cancellationToken)
    {
        Forgotten.Add(protectedKey.ToArray());
        return Task.CompletedTask;
    }

    public Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
    {
        UnprotectCalls++;
        if (Outcome != KeyStoreOutcome.Succeeded)
        {
            return Task.FromResult(Outcome);
        }

        if (protectedKey.Length != NonceLength + dataKey.Length + 16)
        {
            return Task.FromResult(KeyStoreOutcome.KeyNotFound);
        }

        using var key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
        var opened = AeadAlgorithm.XChaCha20Poly1305.Decrypt(key, protectedKey.Span[..NonceLength], [], protectedKey.Span[NonceLength..], dataKey.Span);
        return Task.FromResult(opened ? KeyStoreOutcome.Succeeded : KeyStoreOutcome.KeyNotFound);
    }
}
