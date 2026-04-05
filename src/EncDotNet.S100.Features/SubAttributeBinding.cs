namespace EncDotNet.S100.Features;

public sealed class SubAttributeBinding
{
    public required Multiplicity Multiplicity { get; init; }

    public required string AttributeRef { get; init; }

    public bool Sequential { get; init; }
}
