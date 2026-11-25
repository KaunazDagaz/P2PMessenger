namespace P2PMessenger.Transport.Connections;

internal static class SessionPreference
{
    public static bool NewReplacesExisting(
        ConnectionRoute existingRoute,
        bool existingOpenedHere,
        TimeSpan existingAge,
        ConnectionRoute newRoute,
        bool newOpenedHere,
        bool ownStaticKeyIsLower,
        TimeSpan simultaneousOpenWindow)
    {
        if (newRoute != existingRoute)
        {
            return newRoute == ConnectionRoute.Tor || existingAge >= simultaneousOpenWindow;
        }

        return NewReplacesExisting(existingOpenedHere, existingAge, newOpenedHere, ownStaticKeyIsLower, simultaneousOpenWindow);
    }

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
