using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace P2PMessenger.Spikes.Nearby.Noise;

public sealed class NoiseProtocol
{
    private readonly byte[] _nameBytes;

    private NoiseProtocol(NoiseHashFunction hash)
    {
        Hash = hash;
        Name = "Noise_KK_25519_ChaChaPoly_" + hash.Name;
        _nameBytes = Encoding.ASCII.GetBytes(Name);
    }

    public static NoiseProtocol KkBlake2b { get; } = new(NoiseHashFunction.Blake2b);

    public static NoiseProtocol KkSha256 { get; } = new(NoiseHashFunction.Sha256);

    public static NoiseProtocol KkSha512 { get; } = new(NoiseHashFunction.Sha512);

    public static IReadOnlyList<NoiseProtocol> All { get; } = [KkBlake2b, KkSha256, KkSha512];

    public string Name { get; }

    public NoiseHashFunction Hash { get; }

    public ReadOnlySpan<byte> NameBytes => _nameBytes;

    public static bool TryFind(string name, [NotNullWhen(true)] out NoiseProtocol? protocol)
    {
        protocol = All.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return protocol is not null;
    }

    public override string ToString() => Name;
}
