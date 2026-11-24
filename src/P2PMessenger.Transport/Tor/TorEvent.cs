namespace P2PMessenger.Transport.Tor;

internal sealed record TorEvent(TorEventKind Kind, int Progress = 0, bool Warning = false, string? Reason = null, string? ServiceId = null);
