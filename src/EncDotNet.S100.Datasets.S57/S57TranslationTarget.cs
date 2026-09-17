namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The S-100 product an S-57 dataset is translated into by
/// <see cref="S57ToS101Translator"/>: whose bundled Feature Catalogue the
/// translation checks enumerate values and complex-attribute bindings against,
/// and which product specification the translated document declares.
/// </summary>
/// <remarks>
/// <para>
/// S-101 and S-401 (IEHG inland ENC) share the S-101 in-memory document model,
/// so one translator serves both; only the catalogue it validates against, the
/// mapping it resolves object classes with, and the product it stamps on the
/// output differ. A maritime ENC targets <see cref="S101"/>; an inland ENC
/// targets <see cref="S401"/> (issue #608).
/// </para>
/// </remarks>
public sealed record S57TranslationTarget
{
    /// <summary>
    /// Translation into S-101, validated against the bundled S-101 Feature
    /// Catalogue.
    /// </summary>
    public static S57TranslationTarget S101 { get; } = new()
    {
        Spec = "S-101",
        Edition = "1.0.0",
    };

    /// <summary>
    /// Translation into S-401 (IEHG inland ENC), validated against the bundled
    /// S-401 Feature Catalogue (edition 1.3.0), for S-57 inland ENCs.
    /// </summary>
    public static S57TranslationTarget S401 { get; } = new()
    {
        Spec = "S-401",
        Edition = "1.3.0",
    };

    /// <summary>
    /// The target product specification (e.g. <c>"S-101"</c>). Names the bundled
    /// Feature Catalogue the translation is checked against, and is declared as
    /// the translated document's <c>DSID</c> product specification.
    /// </summary>
    public required string Spec { get; init; }

    /// <summary>
    /// The product specification edition the translated document declares (its
    /// <c>DSID</c> product specification edition).
    /// </summary>
    public required string Edition { get; init; }
}
