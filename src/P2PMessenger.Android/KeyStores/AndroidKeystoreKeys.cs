using System.Runtime.Versioning;
using Android.Content;
using Android.Content.PM;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace P2PMessenger.Android.KeyStores;

internal sealed class AndroidKeystoreKeys : IKeystoreKeys
{
    private const string Provider = "AndroidKeyStore";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int KeySizeBits = 256;
    private const int TagBits = 128;

    public AndroidKeystoreKeys(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HasStrongBox = OperatingSystem.IsAndroidVersionAtLeast(28) && HasStrongBoxFeature(context);
    }

    public bool HasStrongBox { get; }

    public KeystoreStatus Create(string alias, bool strongBox)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);

        try
        {
            using KeyGenParameterSpec.Builder builder = new(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            _ = builder.SetBlockModes(KeyProperties.BlockModeGcm);
            _ = builder.SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone);
            _ = builder.SetKeySize(KeySizeBits);
            _ = builder.SetRandomizedEncryptionRequired(true);
            _ = builder.SetUserAuthenticationRequired(false);
            if (strongBox)
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(28))
                {
                    return KeystoreStatus.StrongBoxUnavailable;
                }

                PreferStrongBox(builder);
            }

            using KeyGenParameterSpec spec = builder.Build();
            using KeyGenerator? generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider);
            if (generator is null)
            {
                return KeystoreStatus.Unavailable;
            }

            generator.Init(spec);
            using ISecretKey? key = generator.GenerateKey();
            return key is null ? KeystoreStatus.Unavailable : KeystoreStatus.Succeeded;
        }
        catch (Java.Lang.Exception exception)
        {
            return IsStrongBoxUnavailable(exception) ? KeystoreStatus.StrongBoxUnavailable : KeystoreStatus.Unavailable;
        }
    }

    public KeystoreStatus Encrypt(string alias, byte[] associatedData, byte[] plaintext, out byte[] nonce, out byte[] ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        ArgumentNullException.ThrowIfNull(associatedData);
        ArgumentNullException.ThrowIfNull(plaintext);

        nonce = [];
        ciphertext = [];
        try
        {
            using KeyStore? store = OpenStore();
            using IKey? key = store?.GetKey(alias, null);
            using Cipher? cipher = Cipher.GetInstance(Transformation);
            if (store is null || cipher is null)
            {
                return KeystoreStatus.Unavailable;
            }

            if (key is null)
            {
                return KeystoreStatus.NotFound;
            }

            cipher.Init(CipherMode.EncryptMode, key);
            cipher.UpdateAAD(associatedData);
            byte[]? sealedBytes = cipher.DoFinal(plaintext);
            byte[]? iv = cipher.GetIV();
            if (sealedBytes is null || iv is null)
            {
                return KeystoreStatus.Unavailable;
            }

            nonce = iv;
            ciphertext = sealedBytes;
            return KeystoreStatus.Succeeded;
        }
        catch (Java.Lang.Exception)
        {
            return KeystoreStatus.Unavailable;
        }
    }

    public KeystoreStatus Decrypt(string alias, byte[] nonce, byte[] associatedData, byte[] ciphertext, out byte[] plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(associatedData);
        ArgumentNullException.ThrowIfNull(ciphertext);

        plaintext = [];
        try
        {
            using KeyStore? store = OpenStore();
            using IKey? key = store?.GetKey(alias, null);
            using Cipher? cipher = Cipher.GetInstance(Transformation);
            if (store is null || cipher is null)
            {
                return KeystoreStatus.Unavailable;
            }

            if (key is null)
            {
                return KeystoreStatus.NotFound;
            }

            using GCMParameterSpec parameters = new(TagBits, nonce);
            cipher.Init(CipherMode.DecryptMode, key, parameters);
            cipher.UpdateAAD(associatedData);
            plaintext = cipher.DoFinal(ciphertext) ?? [];
            return KeystoreStatus.Succeeded;
        }
        catch (AEADBadTagException)
        {
            return KeystoreStatus.Rejected;
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            return KeystoreStatus.Rejected;
        }
        catch (UnrecoverableKeyException)
        {
            return KeystoreStatus.Rejected;
        }
        catch (Java.Lang.Exception)
        {
            return KeystoreStatus.Unavailable;
        }
    }

    public void Delete(string alias)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);

        try
        {
            using KeyStore? store = OpenStore();
            if (store is not null && store.ContainsAlias(alias))
            {
                store.DeleteEntry(alias);
            }
        }
        catch (Java.Lang.Exception)
        {
            return;
        }
    }

    private static KeyStore? OpenStore()
    {
        KeyStore? store = KeyStore.GetInstance(Provider);
        store?.Load(null);
        return store;
    }

    [SupportedOSPlatform("android28.0")]
    private static bool HasStrongBoxFeature(Context context) =>
        context.PackageManager?.HasSystemFeature(PackageManager.FeatureStrongboxKeystore) == true;

    [SupportedOSPlatform("android28.0")]
    private static void PreferStrongBox(KeyGenParameterSpec.Builder builder) => _ = builder.SetIsStrongBoxBacked(true);

    private static bool IsStrongBoxUnavailable(Java.Lang.Exception exception) =>
        OperatingSystem.IsAndroidVersionAtLeast(28) && exception is StrongBoxUnavailableException;
}
