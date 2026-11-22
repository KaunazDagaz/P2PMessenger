using System.Runtime.Versioning;
using System.Security.Cryptography;
using P2PMessenger.Application.LocalData;
using P2PMessenger.Desktop.KeyStores;

namespace P2PMessenger.Desktop.Tests.KeyStores;

[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtectorTests
{
    [Fact]
    [Trait("Story", "US-20")]
    public async Task Protect_ThenUnprotect_GivesTheDataKeyBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI exists only on Windows.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new DpapiKeyProtector();
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var protectedKey = await protector.ProtectAsync(dataKey, cancellationToken);
        var unprotected = new byte[32];
        var outcome = await protector.UnprotectAsync(protectedKey.ProtectedKey, unprotected, cancellationToken);

        Assert.True(protectedKey.Succeeded);
        Assert.Equal(-1, protectedKey.ProtectedKey.Span.IndexOf(dataKey));
        Assert.Equal(KeyStoreOutcome.Succeeded, outcome);
        Assert.Equal(dataKey, unprotected);
    }

    [Fact]
    [Trait("Story", "US-20")]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_ChangedBlob_SaysTheKeyIsNotThere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI exists only on Windows.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new DpapiKeyProtector();
        var protectedKey = (await protector.ProtectAsync(RandomNumberGenerator.GetBytes(32), cancellationToken)).ProtectedKey.ToArray();
        protectedKey[^1] ^= 1;

        var outcome = await protector.UnprotectAsync(protectedKey, new byte[32], cancellationToken);

        Assert.Equal(KeyStoreOutcome.KeyNotFound, outcome);
    }

    [Fact]
    [Trait("NFR", "NFR-11")]
    public async Task Unprotect_BlobOfAnotherApp_SaysTheKeyIsNotThere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI exists only on Windows.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var foreign = ProtectedData.Protect(RandomNumberGenerator.GetBytes(32), "another app"u8.ToArray(), DataProtectionScope.CurrentUser);

        var outcome = await new DpapiKeyProtector().UnprotectAsync(foreign, new byte[32], cancellationToken);

        Assert.Equal(KeyStoreOutcome.KeyNotFound, outcome);
    }

    [Fact]
    [Trait("Story", "US-20")]
    public async Task Forget_ProtectedKey_LeavesItReadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI exists only on Windows.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new DpapiKeyProtector();
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var protectedKey = (await protector.ProtectAsync(dataKey, cancellationToken)).ProtectedKey;

        await protector.ForgetAsync(protectedKey, cancellationToken);
        var unprotected = new byte[32];

        Assert.Equal(KeyStoreOutcome.Succeeded, await protector.UnprotectAsync(protectedKey, unprotected, cancellationToken));
        Assert.Equal(dataKey, unprotected);
    }
}
