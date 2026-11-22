using System.Globalization;
using System.Security.Cryptography;
using NSec.Cryptography;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.UI.Tests;

internal sealed class SavedData : IDisposable
{
    public const string Passphrase = "lantern orchard pebble";

    private static readonly PassphraseKdf Cheap = new(1024, 1);

    public SavedData()
    {
        Folder = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"p2pmessenger-ui-test-{Guid.NewGuid():N}"));
    }

    public string Folder { get; }

    public string DatabasePath => Path.Combine(Folder, DatabaseLocalData.FileName);

    public KeyStore Store { get; } = new();

    public DatabaseLocalData Open(bool withKeyStore = true) => new(DatabasePath, withKeyStore ? Store : null, Cheap);

    public void Dispose()
    {
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }
    }

    internal sealed class KeyStore : IKeyProtector
    {
        private const int NonceLength = 24;
        private readonly byte[] deviceKey = RandomNumberGenerator.GetBytes(32);

        public KeyProtection Protection => KeyProtection.WindowsDpapi;

        public KeyStoreOutcome Outcome { get; set; } = KeyStoreOutcome.Succeeded;

        public Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
        {
            if (Outcome != KeyStoreOutcome.Succeeded)
            {
                return Task.FromResult(ProtectResult.Failure(Outcome));
            }

            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            using var key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
            byte[] wrapped = [.. nonce, .. AeadAlgorithm.XChaCha20Poly1305.Encrypt(key, nonce, [], dataKey.Span)];
            return Task.FromResult(ProtectResult.Success(wrapped));
        }

        public Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
        {
            if (Outcome != KeyStoreOutcome.Succeeded)
            {
                return Task.FromResult(Outcome);
            }

            using var key = Key.Import(AeadAlgorithm.XChaCha20Poly1305, deviceKey, KeyBlobFormat.RawSymmetricKey);
            var opened = AeadAlgorithm.XChaCha20Poly1305.Decrypt(key, protectedKey.Span[..NonceLength], [], protectedKey.Span[NonceLength..], dataKey.Span);
            return Task.FromResult(opened ? KeyStoreOutcome.Succeeded : KeyStoreOutcome.KeyNotFound);
        }

        public Task ForgetAsync(ReadOnlyMemory<byte> protectedKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
