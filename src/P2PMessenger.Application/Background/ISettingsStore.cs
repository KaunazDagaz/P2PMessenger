namespace P2PMessenger.Application.Background;

public interface ISettingsStore
{
    bool ReadFlag(string key, bool fallback);

    void WriteFlag(string key, bool value);

    void Clear();
}
