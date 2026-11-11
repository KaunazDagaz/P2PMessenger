namespace P2PMessenger.Spikes.AndroidHead;

public enum ServiceKind
{
    ConnectedDevice,
    SpecialUse,
    Untyped,
}

public sealed record ServiceRequest(ServiceKind Kind, bool WakeLock, int Port);
