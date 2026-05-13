namespace EncDotNet.S100.Features;

public sealed class InformationType
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public string? Alias { get; init; }

    public string? Remarks { get; init; }

    public bool IsAbstract { get; init; }

    /// <summary>The code of the super type, if any.</summary>
    public string? SuperType { get; init; }

    public IReadOnlyList<AttributeBinding> AttributeBindings { get; init; } = [];

    public IReadOnlyList<InformationBinding> InformationBindings { get; init; } = [];
}
