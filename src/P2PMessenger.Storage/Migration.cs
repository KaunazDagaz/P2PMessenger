namespace P2PMessenger.Storage;

public sealed record Migration(int Version, string Name, string Script);
