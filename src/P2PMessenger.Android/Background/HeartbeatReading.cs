namespace P2PMessenger.Android.Background;

public sealed record HeartbeatReading(
    int Number,
    TimeSpan Lateness,
    TimeSpan Asleep,
    bool DeviceIdle,
    bool Interactive,
    bool PowerSave,
    bool Exempt,
    bool Restricted,
    int BatteryPercent,
    bool Charging);
