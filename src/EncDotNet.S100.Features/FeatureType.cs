namespace EncDotNet.S100.Features;

public sealed class FeatureType
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public string? Alias { get; init; }

    public string? Remarks { get; init; }

    public bool IsAbstract { get; init; }

    public IReadOnlyList<AttributeBinding> AttributeBindings { get; init; } = [];

    public IReadOnlyList<FeatureBinding> FeatureBindings { get; init; } = [];

    public IReadOnlyList<InformationBinding> InformationBindings { get; init; } = [];

    public string? FeatureUseType { get; init; }

    public IReadOnlyList<string> PermittedPrimitives { get; init; } = [];
}
