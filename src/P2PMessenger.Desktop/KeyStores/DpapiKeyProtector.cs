using System.Runtime.Versioning;
using System.Security.Cryptography;
using P2PMessenger.Application.LocalData;

namespace P2PMessenger.Desktop.KeyStores;

[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    public KeyProtection Protection => KeyProtection.WindowsDpapi;

    private static byte[] Entropy => "P2PM/data-key/dpapi/v1"u8.ToArray();

    public Task<ProtectResult> ProtectAsync(ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken)
    {
        byte[] plain = dataKey.ToArray();
        try
        {
            return Task.FromResult(ProtectResult.Success(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)));
        }
        catch (CryptographicException)
        {
            return Task.FromResult(ProtectResult.Failure(KeyStoreOutcome.Refused));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task<KeyStoreOutcome> UnprotectAsync(ReadOnlyMemory<byte> protectedKey, Memory<byte> dataKey, CancellationToken cancellationToken)
    {
        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(protectedKey.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return Task.FromResult(KeyStoreOutcome.KeyNotFound);
        }

        try
        {
            if (plain.Length != dataKey.Length)
            {
                return Task.FromResult(KeyStoreOutcome.KeyNotFound);
            }

            plain.CopyTo(dataKey);
            return Task.FromResult(KeyStoreOutcome.Succeeded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
