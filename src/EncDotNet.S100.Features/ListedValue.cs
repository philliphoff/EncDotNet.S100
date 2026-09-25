namespace EncDotNet.S100.Features;

/// <summary>
/// One enumerant of an enumerated <see cref="SimpleAttribute"/>, parsed from
/// <c>listedValues/listedValue</c>. Dataset values carry the numeric <see cref="Code"/>;
/// <see cref="FeatureCatalogueDecoder"/> maps it back to <see cref="Label"/> or
/// <see cref="Definition"/>.
/// </summary>
public sealed class ListedValue
{
    /// <summary>Display label of the value (<c>label</c>), e.g. <c>"In force"</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Prose definition of the value (<c>definition</c>), or <see langword="null"/> when the catalogue omits it.</summary>
    public string? Definition { get; init; }

    /// <summary>Code that identifies the value within its attribute (<c>code</c>), typically a small integer such as <c>"1"</c>.</summary>
    public required string Code { get; init; }
}
