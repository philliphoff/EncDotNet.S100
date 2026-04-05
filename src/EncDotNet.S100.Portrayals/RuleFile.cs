namespace EncDotNet.S100.Portrayals;

public sealed class RuleFile
{
    public required string Id { get; init; }

    public required Description Description { get; init; }

    public required string FileName { get; init; }

    public required string FileType { get; init; }

    public required string FileFormat { get; init; }

    public required string RuleType { get; init; }
}
