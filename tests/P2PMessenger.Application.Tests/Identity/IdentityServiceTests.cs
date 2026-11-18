using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Contacts;
using P2PMessenger.Core.Identity;

namespace P2PMessenger.Application.Tests.Identity;

public sealed class IdentityServiceTests
{
    [Fact]
    [Trait("Story", "US-01")]
    public async Task CreateAsync_ValidName_CreatesKeysCodeAndFingerprintOnTheDevice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new IdentityService(new InMemoryIdentityStore());

        var result = await service.CreateAsync("  Alice  ", cancellationToken);

        Assert.True(result.IsCreated);
        using var identity = result.Identity;
        Assert.Equal(CreateIdentityError.None, result.Error);
        Assert.Equal("Alice", identity.Name.Value);
        Assert.True(ContactCode.TryParseText(identity.CodeText, out var code, out _));
        Assert.Equal(identity.Keys.IdentityKey, code.IdentityKey);
        Assert.Equal(identity.Keys.StaticKey, code.StaticKey);
        Assert.Equal(identity.Keys.OnionAddress, code.OnionAddress);
        Assert.Equal("Alice", code.DisplayName.Value);
        Assert.Equal(Fingerprint.Of(identity.Keys.IdentityKey), identity.Fingerprint);
        Assert.Same(identity, await service.LoadAsync(cancellationToken));
    }

    [Theory]
    [Trait("Story", "US-01")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ali\u0007ce")]
    [InlineData("A\nB")]
    public async Task CreateAsync_InvalidName_CreatesNothing(string? name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new IdentityService(new InMemoryIdentityStore());

        var result = await service.CreateAsync(name, cancellationToken);

        Assert.False(result.IsCreated);
        Assert.Equal(CreateIdentityError.InvalidName, result.Error);
        Assert.Null(await service.LoadAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-01")]
    public async Task CreateAsync_NameLongerThan64Bytes_CreatesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new IdentityService(new InMemoryIdentityStore());

        var result = await service.CreateAsync(new string('Ж', 33), cancellationToken);

        Assert.Equal(CreateIdentityError.InvalidName, result.Error);
    }

    [Fact]
    [Trait("Story", "US-01")]
    public async Task CreateAsync_Twice_KeepsTheFirstIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new IdentityService(new InMemoryIdentityStore());
        var first = await service.CreateAsync("Alice", cancellationToken);

        var second = await service.CreateAsync("Bob", cancellationToken);

        Assert.True(first.IsCreated);
        using var identity = first.Identity;
        Assert.Equal(CreateIdentityError.AlreadyCreated, second.Error);
        Assert.Same(identity, await service.LoadAsync(cancellationToken));
    }

    [Fact]
    [Trait("Story", "US-01")]
    public async Task CreateAsync_ManyAtOnce_SavesExactlyOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new IdentityService(new InMemoryIdentityStore());

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => service.CreateAsync($"Name {index}", cancellationToken)));

        var created = Assert.Single(results, static result => result.IsCreated);
        using var identity = created.Identity;
        Assert.All(results.Where(static result => !result.IsCreated), static result => Assert.Equal(CreateIdentityError.AlreadyCreated, result.Error));
        Assert.Same(identity, await service.LoadAsync(cancellationToken));
    }
}
