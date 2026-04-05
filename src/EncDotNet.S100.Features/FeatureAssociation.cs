namespace EncDotNet.S100.Features;

public sealed class FeatureAssociation
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public bool IsAbstract { get; init; }

    public IReadOnlyList<string> RoleRefs { get; init; } = [];
}
