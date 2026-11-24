using System.Text;
using P2PMessenger.Application.Background;

namespace P2PMessenger.Desktop.Settings;

public sealed class FileSettingsStore : ISettingsStore
{
    public const string FileName = "settings.txt";

    private const int MaxFileLength = 4096;

    private readonly string path;
    private readonly Lock gate = new();

    public FileSettingsStore(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        path = Path.Combine(folder, FileName);
    }

    public string FilePath => path;

    public bool ReadFlag(string key, bool fallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (gate)
        {
            return Load().TryGetValue(key, out bool value) ? value : fallback;
        }
    }

    public void WriteFlag(string key, bool value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.Contains('=', StringComparison.Ordinal) || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A setting key has no '=' and no white space.", nameof(key));
        }

        lock (gate)
        {
            Dictionary<string, bool> flags = Load();
            flags[key] = value;
            Save(flags);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private Dictionary<string, bool> Load()
    {
        Dictionary<string, bool> flags = new(StringComparer.Ordinal);
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length > MaxFileLength)
            {
                return flags;
            }

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0 && bool.TryParse(line.AsSpan(separator + 1), out bool value))
                {
                    flags[line[..separator]] = value;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return flags;
    }

    private void Save(Dictionary<string, bool> flags)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string temporary = path + ".new";
        File.WriteAllLines(temporary, flags.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Key + "=" + (pair.Value ? "true" : "false")), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
