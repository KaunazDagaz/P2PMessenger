namespace P2PMessenger.Android.KeyStores;

internal interface IKeystoreKeys
{
    bool HasStrongBox { get; }

    KeystoreStatus Create(string alias, bool strongBox);

    KeystoreStatus Encrypt(string alias, byte[] associatedData, byte[] plaintext, out byte[] nonce, out byte[] ciphertext);

    KeystoreStatus Decrypt(string alias, byte[] nonce, byte[] associatedData, byte[] ciphertext, out byte[] plaintext);

    void Delete(string alias);
}
