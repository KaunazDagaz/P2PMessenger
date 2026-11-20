using System.Runtime.Versioning;
using System.Security.Cryptography;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Desktop.KeyStores;
using Tmds.DBus.Protocol;

namespace P2PMessenger.Desktop.Tests.KeyStores;

[SupportedOSPlatform("linux")]
public sealed class SecretServiceKeyProtectorTests
{
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_NoSessionBus_SaysTheKeyStoreIsUnavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is used on Linux only.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new SecretServiceKeyProtector(null, PromptTimeout);

        var wellFormed = new byte[SecretServiceKeyProtector.ProtectedKeyLength];
        wellFormed[0] = 1;

        var protectedKey = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);
        var outcome = await protector.UnprotectAsync(wellFormed, new byte[32], cancellationToken);

        Assert.Equal(KeyStoreOutcome.Unavailable, protectedKey.Outcome);
        Assert.Equal(KeyStoreOutcome.Unavailable, outcome);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_SessionBusThatIsNotThere_SaysTheKeyStoreIsUnavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is used on Linux only.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-bus-{Guid.NewGuid():N}");
        var protector = new SecretServiceKeyProtector($"unix:path={missing}", PromptTimeout);

        var protectedKey = await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        Assert.Equal(KeyStoreOutcome.Unavailable, protectedKey.Outcome);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_BlobOfAnotherFormat_SaysTheKeyIsNotThereWithoutAskingTheKeyStore()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is used on Linux only.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new SecretServiceKeyProtector(null, PromptTimeout);

        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(new byte[10], new byte[32], cancellationToken));
        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(new byte[SecretServiceKeyProtector.ProtectedKeyLength], new byte[32], cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_ThenUnprotect_GivesTheDataKeyBack()
    {
        var protector = await RunningSecretServiceAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var first = await protector.ProtectAsync(dataKey, cancellationToken);
        var second = await protector.ProtectAsync(dataKey, cancellationToken);
        var unprotected = new byte[32];
        var outcome = await protector.UnprotectAsync(first.ProtectedKey, unprotected, cancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(SecretServiceKeyProtector.ProtectedKeyLength, first.ProtectedKey.Length);
        Assert.Equal(-1, first.ProtectedKey.Span.IndexOf(dataKey));
        Assert.NotEqual(first.ProtectedKey.Span.Slice(1, SecretServiceKeyProtector.KeyIdLength).ToArray(), second.ProtectedKey.Span.Slice(1, SecretServiceKeyProtector.KeyIdLength).ToArray());
        Assert.Equal(KeyStoreOutcome.Succeeded, outcome);
        Assert.Equal(dataKey, unprotected);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_EveryFlippedBit_IsRefused()
    {
        var protector = await RunningSecretServiceAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var protectedKey = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey.ToArray();

        for (var bit = 0; bit < protectedKey.Length * 8; bit++)
        {
            var changed = (byte[])protectedKey.Clone();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));

            Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(changed, new byte[32], cancellationToken));
        }
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_KeyIdThatTheKeyStoreDoesNotHold_SaysTheKeyIsNotThere()
    {
        var protector = await RunningSecretServiceAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var protectedKey = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey.ToArray();
        RandomNumberGenerator.Fill(protectedKey.AsSpan(1, SecretServiceKeyProtector.KeyIdLength));

        Assert.Equal(KeyStoreOutcome.KeyNotFound, await protector.UnprotectAsync(protectedKey, new byte[32], cancellationToken));
    }

    private static async Task<SecretServiceKeyProtector> RunningSecretServiceAsync()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is used on Linux only.");
        Assert.SkipWhen(string.IsNullOrEmpty(DBusAddress.Session), "No D-Bus session bus is running.");

        var protector = new SecretServiceKeyProtector(DBusAddress.Session, PromptTimeout);
        var probe = await protector.ProtectAsync(new byte[32], TestContext.Current.CancellationToken);
        Assert.SkipUnless(probe.Succeeded, $"No unlocked Secret Service answers on the session bus ({probe.Outcome}).");
        return protector;
    }
}
