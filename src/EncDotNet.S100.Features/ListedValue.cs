namespace EncDotNet.S100.Features;

public sealed class ListedValue
{
    public required string Label { get; init; }

    public string? Definition { get; init; }

    public required string Code { get; init; }
}
