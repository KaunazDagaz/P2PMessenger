namespace P2PMessenger.Core;

public enum FailureSource
{
    UiThread = 0,
    BackgroundTask = 1,
    AppDomain = 2,
    Connections = 3,
    Discovery = 4,
    Delivery = 5,
    Groups = 6,
    PcCopy = 7,
}
