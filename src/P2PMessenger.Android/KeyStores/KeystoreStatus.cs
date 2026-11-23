namespace P2PMessenger.Android.KeyStores;

internal enum KeystoreStatus
{
    Succeeded = 0,
    NotFound = 1,
    Rejected = 2,
    StrongBoxUnavailable = 3,
    Unavailable = 4,
}
