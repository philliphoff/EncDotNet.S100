namespace EncDotNet.S100.Features;

/// <summary>
/// The permitted number of occurrences of a bound attribute, feature or information type,
/// parsed from a binding's <c>multiplicity</c> element (<c>S100Base:lower</c> /
/// <c>S100Base:upper</c>). Used by <see cref="AttributeBinding"/>,
/// <see cref="SubAttributeBinding"/>, <see cref="FeatureBinding"/> and
/// <see cref="InformationBinding"/>.
/// </summary>
public sealed class Multiplicity
{
    /// <summary>Minimum number of occurrences (<c>S100Base:lower</c>); <c>0</c> means optional.</summary>
    public required int Lower { get; init; }

    /// <summary>
    /// Maximum number of occurrences (<c>S100Base:upper</c>), or <see langword="null"/> when
    /// the upper bound is unbounded (<see cref="IsInfinite"/>), marked <c>xsi:nil="true"</c>,
    /// absent, or not a parseable integer.
    /// </summary>
    public int? Upper { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>S100Base:upper</c> carries <c>infinite="true"</c>,
    /// i.e. there is no maximum; <see cref="Upper"/> is then <see langword="null"/>.
    /// </summary>
    public bool IsInfinite { get; init; }
}
