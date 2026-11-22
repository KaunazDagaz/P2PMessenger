using System.Text;
using NSec.Cryptography;
using P2PMessenger.Storage.Protection;

namespace P2PMessenger.Storage.Tests.Protection;

public sealed class PassphraseKeyWrapTests
{
    private static readonly PassphraseKdf Cheap = new(1024, 2);

    [Fact]
    public void Argon2id_ReferenceVectorWith64MiBAnd3Passes_GivesTheSameKey()
    {
        var parameters = new Argon2Parameters { DegreeOfParallelism = 1, MemorySize = 65_536, NumberOfPasses = 3 };
        var argon2id = PasswordBasedKeyDerivationAlgorithm.Argon2id(in parameters);

        var key = argon2id.DeriveBytes(Encoding.UTF8.GetBytes(PassphraseVectors.Passphrase), RecordVectors.Bytes(PassphraseVectors.Salt), 32);

        Assert.Equal(PassphraseVectors.Argon2idPasses3Memory64MiB, Convert.ToHexStringLower(key));
    }

    [Fact]
    public void Argon2id_ReferenceVectorWithACyrillicPassphrase_GivesTheSameKey()
    {
        var parameters = new Argon2Parameters { DegreeOfParallelism = 1, MemorySize = 1024, NumberOfPasses = 1 };
        var argon2id = PasswordBasedKeyDerivationAlgorithm.Argon2id(in parameters);

        var key = argon2id.DeriveBytes(RecordVectors.Bytes(PassphraseVectors.CyrillicPassphraseUtf8), RecordVectors.Bytes(PassphraseVectors.SecondSalt), 32);

        Assert.Equal(PassphraseVectors.Argon2idCyrillicPasses1Memory1MiB, Convert.ToHexStringLower(key));
        Assert.Equal(PassphraseVectors.CyrillicPassphraseUtf8, Convert.ToHexStringLower(PassphraseText.Encode("\u043f\u0430\u0440\u043e\u043b\u044c \u0434\u043b\u044f \u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0438")));
    }

    [Fact]
    public void Wrap_KnownInputs_GivesTheIndependentlyComputedBytes()
    {
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);

        var wrapped = PassphraseKeyWrap.Wrap(RecordVectors.Bytes(RecordVectors.DataKey), PassphraseVectors.Passphrase, derivation, RecordVectors.Bytes(PassphraseVectors.Nonce));

        Assert.Equal(PassphraseVectors.AssociatedData, Convert.ToHexStringLower(PassphraseKeyWrap.AssociatedData(derivation)));
        Assert.Equal(PassphraseVectors.Wrapped, Convert.ToHexStringLower(wrapped.WrappedKey.Span));
        Assert.Equal(PassphraseKeyWrap.WrappedLength, wrapped.WrappedKey.Length);
        Assert.Equal(DataKeyProtection.Passphrase, wrapped.Protection);
        Assert.Same(derivation, wrapped.Derivation);
    }

    [Fact]
    public void Wrap_SamePassphraseInAnotherUnicodeForm_GivesTheSameBytes()
    {
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);

        var wrapped = PassphraseKeyWrap.Wrap(RecordVectors.Bytes(RecordVectors.DataKey), PassphraseVectors.FullWidthPassphrase, derivation, RecordVectors.Bytes(PassphraseVectors.Nonce));

        Assert.Equal(PassphraseVectors.Wrapped, Convert.ToHexStringLower(wrapped.WrappedKey.Span));
    }

    [Fact]
    public void TryUnwrap_KnownBytes_GivesTheDataKey()
    {
        var wrapped = KnownWrapped();
        var dataKey = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(PassphraseUnwrapOutcome.Succeeded, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, dataKey));
        Assert.Equal(RecordVectors.DataKey, Convert.ToHexStringLower(dataKey));
    }

    [Fact]
    public void Wrap_DefaultSettings_RoundTripsWithFreshSaltAndNonce()
    {
        var dataKey = RecordVectors.Bytes(RecordVectors.DataKey);

        var first = PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, PassphraseKdf.Default);
        var second = PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, PassphraseKdf.Default);
        var opened = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(new PassphraseKdf(65_536, 3), first.Derivation!.Kdf);
        Assert.NotEqual(first.Derivation.Salt.ToArray(), second.Derivation!.Salt.ToArray());
        Assert.NotEqual(first.WrappedKey.ToArray(), second.WrappedKey.ToArray());
        Assert.Equal(PassphraseUnwrapOutcome.Succeeded, PassphraseKeyWrap.TryUnwrap(first, PassphraseVectors.Passphrase, opened));
        Assert.Equal(dataKey, opened);
    }

    [Theory]
    [InlineData("correct horse battery stapl")]
    [InlineData("correct horse battery staple ")]
    [InlineData("Correct horse battery staple")]
    [InlineData("")]
    public void TryUnwrap_WrongPassphrase_IsRefusedAndGivesNoKey(string passphrase)
    {
        var dataKey = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(KnownWrapped(), passphrase, dataKey));
        Assert.All(dataKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TryUnwrap_PassphraseThatIsNotText_IsRefused()
    {
        var dataKey = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(KnownWrapped(), "correct horse \ud800 staple", dataKey));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryUnwrap_EveryFlippedBitOfTheWrappedKey_IsRefused()
    {
        var known = RecordVectors.Bytes(PassphraseVectors.Wrapped);
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);
        var dataKey = new byte[RecordKeys.DataKeyLength];

        for (var bit = 0; bit < known.Length * 8; bit++)
        {
            var changed = known.ToArray();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            var outcome = PassphraseKeyWrap.TryUnwrap(new WrappedDataKey(DataKeyProtection.Passphrase, changed, derivation), PassphraseVectors.Passphrase, dataKey);

            Assert.Equal(bit < 8 ? PassphraseUnwrapOutcome.Damaged : PassphraseUnwrapOutcome.WrongPassphrase, outcome);
        }
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryUnwrap_EveryFlippedBitOfTheSalt_IsRefused()
    {
        var known = RecordVectors.Bytes(PassphraseVectors.Wrapped);
        var salt = RecordVectors.Bytes(PassphraseVectors.Salt);
        var dataKey = new byte[RecordKeys.DataKeyLength];

        for (var bit = 0; bit < salt.Length * 8; bit++)
        {
            var changed = salt.ToArray();
            changed[bit / 8] ^= (byte)(1 << (bit % 8));
            var wrapped = new WrappedDataKey(DataKeyProtection.Passphrase, known, new PassphraseDerivation(changed, Cheap));

            Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, dataKey));
        }
    }

    [Theory]
    [Trait("NFR", "NFR-09")]
    [InlineData(1025L, 2L)]
    [InlineData(1024L, 1L)]
    [InlineData(1024L, 3L)]
    [InlineData(2048L, 2L)]
    public void TryUnwrap_ChangedKdfSettings_AreRefused(long memoryKib, long passes)
    {
        var wrapped = new WrappedDataKey(
            DataKeyProtection.Passphrase,
            RecordVectors.Bytes(PassphraseVectors.Wrapped),
            new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), new PassphraseKdf(memoryKib, passes)));

        Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, new byte[RecordKeys.DataKeyLength]));
    }

    [Fact]
    public void TryUnwrap_EveryTruncationAndAnExtension_IsDamaged()
    {
        var known = RecordVectors.Bytes(PassphraseVectors.Wrapped);
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);
        var dataKey = new byte[RecordKeys.DataKeyLength];

        for (var length = 1; length < known.Length; length++)
        {
            Assert.Equal(PassphraseUnwrapOutcome.Damaged, PassphraseKeyWrap.TryUnwrap(new WrappedDataKey(DataKeyProtection.Passphrase, known.AsSpan(0, length), derivation), PassphraseVectors.Passphrase, dataKey));
        }

        Assert.Equal(PassphraseUnwrapOutcome.Damaged, PassphraseKeyWrap.TryUnwrap(new WrappedDataKey(DataKeyProtection.Passphrase, [.. known, 0], derivation), PassphraseVectors.Passphrase, dataKey));
    }

    [Fact]
    public void WrapWithKeyStore_KnownInputs_GivesTheIndependentlyComputedBytes()
    {
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);

        var wrapped = PassphraseKeyWrap.Wrap(
            RecordVectors.Bytes(RecordVectors.DataKey),
            PassphraseVectors.Passphrase,
            derivation,
            DataKeyProtection.LinuxSecretService,
            RecordVectors.Bytes(PassphraseVectors.Share),
            RecordVectors.Bytes(PassphraseVectors.ProtectedShare),
            RecordVectors.Bytes(PassphraseVectors.Nonce));

        Assert.Equal(PassphraseVectors.KeyStoreAssociatedData, Convert.ToHexStringLower(PassphraseKeyWrap.KeyStoreAssociatedData(derivation, DataKeyProtection.LinuxSecretService)));
        Assert.Equal(PassphraseVectors.WrappedWithKeyStore, Convert.ToHexStringLower(wrapped.WrappedKey.Span));
        Assert.True(PassphraseKeyWrap.TryGetKeyStoreShare(wrapped, out var keyStore, out var protectedShare));
        Assert.Equal(DataKeyProtection.LinuxSecretService, keyStore);
        Assert.Equal(PassphraseVectors.ProtectedShare, Convert.ToHexStringLower(protectedShare.Span));
    }

    [Fact]
    public void TryUnwrapWithKeyStore_NeedsBothThePassphraseAndTheShare()
    {
        var wrapped = KnownWrappedWithKeyStore();
        var share = RecordVectors.Bytes(PassphraseVectors.Share);
        var otherShare = share.ToArray();
        otherShare[0] ^= 1;
        var dataKey = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(wrapped, "violet harbour 4471 kite", share, dataKey));
        Assert.Equal(PassphraseUnwrapOutcome.WrongPassphrase, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, otherShare, dataKey));
        Assert.All(dataKey, value => Assert.Equal(0, value));
        Assert.Equal(PassphraseUnwrapOutcome.Succeeded, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, share, dataKey));
        Assert.Equal(RecordVectors.DataKey, Convert.ToHexStringLower(dataKey));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, new byte[RecordKeys.DataKeyLength]));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.TryUnwrap(KnownWrapped(), PassphraseVectors.Passphrase, share, new byte[RecordKeys.DataKeyLength]));
    }

    [Fact]
    public void WrapWithKeyStore_DefaultSettings_RoundTrips()
    {
        var dataKey = RecordVectors.Bytes(RecordVectors.DataKey);
        var share = RecordVectors.Bytes(PassphraseVectors.Share);

        var wrapped = PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, PassphraseKdf.Default, DataKeyProtection.WindowsDpapi, share, new byte[200]);
        var opened = new byte[RecordKeys.DataKeyLength];

        Assert.Equal(4 + 200 + 72, wrapped.WrappedKey.Length);
        Assert.Equal(PassphraseUnwrapOutcome.Succeeded, PassphraseKeyWrap.TryUnwrap(wrapped, PassphraseVectors.Passphrase, share, opened));
        Assert.Equal(dataKey, opened);
        Assert.False(PassphraseKeyWrap.TryGetKeyStoreShare(KnownWrapped(), out _, out _));
    }

    [Fact]
    [Trait("NFR", "NFR-09")]
    public void TryUnwrapWithKeyStore_EveryFlippedBitOutsideTheProtectedShare_IsRefused()
    {
        var known = RecordVectors.Bytes(PassphraseVectors.WrappedWithKeyStore);
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);
        var share = RecordVectors.Bytes(PassphraseVectors.Share);
        var protectedShareEnd = 4 + RecordVectors.Bytes(PassphraseVectors.ProtectedShare).Length;
        var dataKey = new byte[RecordKeys.DataKeyLength];

        for (var bit = 0; bit < known.Length * 8; bit++)
        {
            var index = bit / 8;
            if (index >= 4 && index < protectedShareEnd)
            {
                continue;
            }

            var changed = known.ToArray();
            changed[index] ^= (byte)(1 << (bit % 8));
            var outcome = PassphraseKeyWrap.TryUnwrap(new WrappedDataKey(DataKeyProtection.Passphrase, changed, derivation), PassphraseVectors.Passphrase, share, dataKey);

            Assert.NotEqual(PassphraseUnwrapOutcome.Succeeded, outcome);
        }
    }

    [Theory]
    [InlineData("01")]
    [InlineData("020100")]
    [InlineData("0205")]
    [InlineData("0204ffff")]
    [InlineData("02040000")]
    [InlineData("02040015")]
    [InlineData("02040013")]
    public void IsWellFormed_KeyStoreFormatWithAnotherHeader_IsNot(string header)
    {
        var known = RecordVectors.Bytes(PassphraseVectors.WrappedWithKeyStore);
        var changed = known.ToArray();
        var replacement = RecordVectors.Bytes(header);
        replacement.CopyTo(changed, 0);

        Assert.True(PassphraseKeyWrap.IsWellFormed(known));
        Assert.False(PassphraseKeyWrap.IsWellFormed(changed));
        Assert.False(PassphraseKeyWrap.IsWellFormed(known.AsSpan(0, known.Length - 1)));
    }

    [Fact]
    public void WrapWithKeyStore_WrongInputs_AreRefused()
    {
        var derivation = new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap);
        var dataKey = RecordVectors.Bytes(RecordVectors.DataKey);
        var nonce = RecordVectors.Bytes(PassphraseVectors.Nonce);

        Assert.Throws<ArgumentOutOfRangeException>(() => PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, derivation, DataKeyProtection.Passphrase, new byte[32], [1], nonce));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, derivation, DataKeyProtection.WindowsDpapi, new byte[31], [1], nonce));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, derivation, DataKeyProtection.WindowsDpapi, new byte[32], [], nonce));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.Wrap(dataKey, PassphraseVectors.Passphrase, derivation, DataKeyProtection.WindowsDpapi, new byte[32], new byte[4097], nonce));
    }

    [Theory]
    [InlineData(7L, 1L)]
    [InlineData(1_048_577L, 1L)]
    [InlineData(1024L, 0L)]
    [InlineData(1024L, 17L)]
    public void Derivation_SettingsOutsideTheRange_AreRefused(long memoryKib, long passes)
    {
        var kdf = new PassphraseKdf(memoryKib, passes);

        Assert.False(kdf.IsAcceptable);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PassphraseDerivation(new byte[PassphraseDerivation.SaltLength], kdf));
    }

    [Fact]
    public void WrappedDataKey_PassphraseAndDerivation_GoTogether()
    {
        var derivation = new PassphraseDerivation(new byte[PassphraseDerivation.SaltLength], Cheap);

        Assert.Throws<ArgumentException>(() => new WrappedDataKey(DataKeyProtection.Passphrase, [1]));
        Assert.Throws<ArgumentException>(() => new WrappedDataKey(DataKeyProtection.WindowsDpapi, [1], derivation));
        Assert.Throws<ArgumentException>(() => new PassphraseDerivation(new byte[15], Cheap));
        Assert.Throws<ArgumentException>(() => PassphraseKeyWrap.TryUnwrap(new WrappedDataKey(DataKeyProtection.WindowsDpapi, [1]), PassphraseVectors.Passphrase, new byte[32]));
    }

    [Theory]
    [InlineData("correct horse battery staple", 28)]
    [InlineData("\ufb01\ufb01\ufb01", 6)]
    [InlineData("e\u0301", 1)]
    [InlineData("\ud83d\udd11\ud83d\udd11", 2)]
    public void CountCharacters_CountsCodePointsAfterNfkc(string passphrase, int expected)
    {
        Assert.Equal(expected, PassphraseText.CountCharacters(passphrase));
    }

    [Fact]
    public void CountCharacters_LoneSurrogates_AreNotText()
    {
        Assert.Equal(-1, PassphraseText.CountCharacters(new string(['a', '\udc00', 'b'])));
        Assert.Equal(-1, PassphraseText.CountCharacters(new string(['a', '\ud800'])));
        Assert.Equal(-1, PassphraseText.CountCharacters(new string(['\udc00', '\ud800'])));
        Assert.False(PassphraseText.TryNormalize(new string(['\ud800', 'x']), out _));
        Assert.Throws<ArgumentException>(() => PassphraseText.Encode(new string(['\ud800'])));
    }

    private static WrappedDataKey KnownWrappedWithKeyStore() =>
        new(
            DataKeyProtection.Passphrase,
            RecordVectors.Bytes(PassphraseVectors.WrappedWithKeyStore),
            new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap));

    private static WrappedDataKey KnownWrapped() =>
        new(
            DataKeyProtection.Passphrase,
            RecordVectors.Bytes(PassphraseVectors.Wrapped),
            new PassphraseDerivation(RecordVectors.Bytes(PassphraseVectors.Salt), Cheap));
}
