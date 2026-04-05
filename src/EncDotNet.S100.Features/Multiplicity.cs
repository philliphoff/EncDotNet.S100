namespace EncDotNet.S100.Features;

public sealed class Multiplicity
{
    public required int Lower { get; init; }

    public int? Upper { get; init; }

    public bool IsInfinite { get; init; }
}
