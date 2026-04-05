namespace EncDotNet.S100.Features;

public sealed class ComplexAttribute
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }

    public string? Alias { get; init; }

    public string? Remarks { get; init; }

    public IReadOnlyList<SubAttributeBinding> SubAttributeBindings { get; init; } = [];
}
