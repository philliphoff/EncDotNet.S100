namespace EncDotNet.S100.Portrayals;

public sealed class ContextParameter
{
    public required string Id { get; init; }

    public required Description Description { get; init; }

    public required string Type { get; init; }

    public required string Default { get; init; }

    public string? Enable { get; init; }

    public ContextParameterValidation? Validation { get; init; }
}
