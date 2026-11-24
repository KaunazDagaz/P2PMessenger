using Android.Content;
using P2PMessenger.Application.Background;

namespace P2PMessenger.Android.Background;

internal sealed class PreferenceFlags : ISettingsStore
{
    private readonly ISharedPreferences preferences;

    public PreferenceFlags(Context context, string fileName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        preferences = context.GetSharedPreferences(fileName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Android returned no preferences file.");
    }

    public bool ReadFlag(string key, bool fallback) => preferences.GetBoolean(key, fallback);

    public void WriteFlag(string key, bool value)
    {
        using ISharedPreferencesEditor? editor = preferences.Edit();
        _ = editor?.PutBoolean(key, value)?.Commit();
    }

    public void Clear()
    {
        using ISharedPreferencesEditor? editor = preferences.Edit();
        _ = editor?.Clear()?.Commit();
    }
}
