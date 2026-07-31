namespace EncDotNet.S100.ExchangeSets;

public sealed class PtLocale
{
    public required string Language { get; init; }

    public string? Country { get; init; }

    public required string CharacterEncoding { get; init; }
}
