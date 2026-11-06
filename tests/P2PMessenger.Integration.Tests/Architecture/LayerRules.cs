namespace P2PMessenger.Integration.Tests.Architecture;

internal static class LayerRules
{
    public const string Core = "P2PMessenger.Core";
    public const string Transport = "P2PMessenger.Transport";
    public const string Storage = "P2PMessenger.Storage";
    public const string Application = "P2PMessenger.Application";
    public const string UI = "P2PMessenger.UI";
    public const string Desktop = "P2PMessenger.Desktop";
    public const string Android = "P2PMessenger.Android";

    public static IReadOnlyList<SourceProjectRule> SourceProjects { get; } =
    [
        new(Core, "net10.0", []),
        new(Transport, "net10.0", [Core]),
        new(Storage, "net10.0", [Core]),
        new(Application, "net10.0", [Core, Transport, Storage]),
        new(UI, "net10.0", [Application]),
        new(Desktop, "net10.0", [UI]),
        new(Android, "net10.0-android36.1", [UI]),
    ];

    public static IReadOnlyList<string> PlainLayers { get; } = [Core, Transport, Storage, Application];
}

internal sealed record SourceProjectRule(string Name, string TargetFramework, IReadOnlyList<string> MayReference);
