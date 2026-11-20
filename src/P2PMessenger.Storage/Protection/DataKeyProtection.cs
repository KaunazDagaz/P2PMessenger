namespace P2PMessenger.Storage.Protection;

public enum DataKeyProtection
{
    Passphrase = 1,
    WindowsDpapi = 2,
    AndroidKeystore = 3,
    LinuxSecretService = 4,
}
