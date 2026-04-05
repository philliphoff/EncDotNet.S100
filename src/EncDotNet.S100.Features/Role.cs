namespace EncDotNet.S100.Features;

public sealed class Role
{
    public required string Name { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }
}
