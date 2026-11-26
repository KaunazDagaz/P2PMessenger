using System.Diagnostics.CodeAnalysis;
using NSec.Cryptography;

namespace P2PMessenger.Core.Groups;

public sealed class GroupKey : IDisposable
{
    public const int Length = 32;
    public const int CheckLength = 32;
    public const int NonceLength = 24;
    public const int TagLength = 16;

    private readonly Key key;
    private readonly byte[] check;

    private GroupKey(Key key)
    {
        this.key = key;
        Span<byte> raw = stackalloc byte[Length];
        try
        {
            Export(raw);
            check = CheckOf(raw);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public ReadOnlySpan<byte> Check => check;

    private static ReadOnlySpan<byte> CheckLabel => "P2PM/group-key-check/v1"u8;

    private static AeadAlgorithm Algorithm => AeadAlgorithm.XChaCha20Poly1305;

    public static GroupKey Create() => new(Key.Create(Algorithm, ExportableParameters()));

    public static bool TryImport(ReadOnlySpan<byte> raw, [NotNullWhen(true)] out GroupKey? groupKey)
    {
        if (raw.Length != Length
            || !Key.TryImport(Algorithm, raw, KeyBlobFormat.RawSymmetricKey, out Key? key, ExportableParameters())
            || key is null)
        {
            groupKey = null;
            return false;
        }

        groupKey = new GroupKey(key);
        return true;
    }

    public void Export(Span<byte> destination)
    {
        if (destination.Length != Length || !key.TryExport(KeyBlobFormat.RawSymmetricKey, destination, out int written) || written != Length)
        {
            throw new ArgumentException("The destination must hold exactly one group key.", nameof(destination));
        }
    }

    public void Dispose() => key.Dispose();

    internal void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext) =>
        Algorithm.Encrypt(key, nonce, associatedData, plaintext, ciphertext);

    internal bool TryDecrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext) =>
        Algorithm.Decrypt(key, nonce, associatedData, ciphertext, plaintext);

    private static byte[] CheckOf(ReadOnlySpan<byte> raw)
    {
        byte[] input = new byte[CheckLabel.Length + raw.Length];
        try
        {
            CheckLabel.CopyTo(input);
            raw.CopyTo(input.AsSpan(CheckLabel.Length));
            return HashAlgorithm.Blake2b_256.Hash(input);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(input);
        }
    }

    private static KeyCreationParameters ExportableParameters() => new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
}
