namespace P2PMessenger.UI.Services;

public static class BuildInfo
{
#if DEBUG
    public static bool IsDebug => true;
#else
    public static bool IsDebug => false;
#endif
}
