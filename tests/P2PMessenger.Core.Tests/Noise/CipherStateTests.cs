using P2PMessenger.Core.Noise;

namespace P2PMessenger.Core.Tests.Noise;

public sealed class CipherStateTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(1, CipherState.KeyLength).Select(static value => (byte)value)];
    private static readonly byte[] Plaintext = "a dummy text that never leaves in the clear"u8.ToArray();

    [Fact]
    public void EncryptWithAd_BeforeAnyKey_PassesThePlaintextThrough()
    {
        using var cipher = new CipherState();
        var output = new byte[Plaintext.Length];

        var written = cipher.EncryptWithAd(default, Plaintext, output);

        Assert.Equal(Plaintext.Length, written);
        Assert.Equal(Convert.ToHexStringLower(Plaintext), Convert.ToHexStringLower(output));
    }

    [Fact]
    public void TryDecryptWithAd_BeforeAnyKey_PassesTheInputThrough()
    {
        using var cipher = new CipherState();
        var output = new byte[Plaintext.Length];

        Assert.True(cipher.TryDecryptWithAd(default, Plaintext, output, out var length));

        Assert.Equal(Plaintext.Length, length);
        Assert.Equal(Convert.ToHexStringLower(Plaintext), Convert.ToHexStringLower(output));
    }

    [Fact]
    [Trait("NFR", "NFR-08")]
    public void EncryptWithAd_AfterDispose_ThrowsAndWritesNothing()
    {
        var cipher = new CipherState();
        cipher.InitializeKey(Key);
        cipher.Dispose();
        var output = new byte[Plaintext.Length + CipherState.TagLength];

        Assert.Throws<ObjectDisposedException>(() => cipher.EncryptWithAd(default, Plaintext, output));

        Assert.Equal(Zeros(output.Length), Convert.ToHexStringLower(output));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryDecryptWithAd_AfterDispose_FailsAndWritesNothing()
    {
        var ciphertext = Encrypt(Plaintext);
        var receiver = new CipherState();
        receiver.InitializeKey(Key);
        receiver.Dispose();
        var output = new byte[ciphertext.Length];

        Assert.False(receiver.TryDecryptWithAd(default, ciphertext, output, out var length));

        Assert.Equal(0, length);
        Assert.Equal(Zeros(output.Length), Convert.ToHexStringLower(output));
        using var control = new CipherState();
        control.InitializeKey(Key);
        Assert.True(control.TryDecryptWithAd(default, ciphertext, output, out _));
    }

    [Fact]
    public void InitializeKey_AfterDispose_Throws()
    {
        var cipher = new CipherState();
        cipher.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cipher.InitializeKey(Key));
        Assert.Throws<ObjectDisposedException>(() => cipher.EncryptWithAd(default, Plaintext, new byte[Plaintext.Length]));
    }

    [Fact]
    [Trait("NFR", "NFR-08")]
    public void TransportCipher_UsedAfterAnotherCallerDisposedIt_NeverWritesPlaintext()
    {
        var sending = new CipherState();
        sending.InitializeKey(Key);
        var receiving = new CipherState();
        receiving.InitializeKey(Key);
        var transport = new TransportCipher(sending, receiving);
        var ciphertext = Encrypt(Plaintext);
        transport.Dispose();
        var message = new byte[Plaintext.Length + CipherState.TagLength];
        var payload = new byte[ciphertext.Length];

        Assert.Throws<ObjectDisposedException>(() => transport.Encrypt(Plaintext, message));
        Assert.False(transport.TryDecrypt(ciphertext, payload, out _));

        Assert.Equal(Zeros(message.Length), Convert.ToHexStringLower(message));
        Assert.Equal(Zeros(payload.Length), Convert.ToHexStringLower(payload));
    }

    private static byte[] Encrypt(byte[] plaintext)
    {
        using var sender = new CipherState();
        sender.InitializeKey(Key);
        var ciphertext = new byte[plaintext.Length + CipherState.TagLength];
        sender.EncryptWithAd(default, plaintext, ciphertext);
        return ciphertext;
    }

    private static string Zeros(int length) => new('0', length * 2);
}
