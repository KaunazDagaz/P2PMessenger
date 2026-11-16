namespace P2PMessenger.Transport.Connections;

internal static class SessionPreference
{
    public static bool NewReplacesExisting(
        bool existingOpenedHere,
        TimeSpan existingAge,
        bool newOpenedHere,
        bool ownStaticKeyIsLower,
        TimeSpan simultaneousOpenWindow)
    {
        if (existingOpenedHere == newOpenedHere || existingAge >= simultaneousOpenWindow)
        {
            return true;
        }

        return newOpenedHere == ownStaticKeyIsLower;
    }
}
