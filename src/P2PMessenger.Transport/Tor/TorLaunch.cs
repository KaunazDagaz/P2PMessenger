namespace P2PMessenger.Transport.Tor;

internal sealed record TorLaunch(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string DataFolder,
    string ControlPortFile,
    string CookieFile);
