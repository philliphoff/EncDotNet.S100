using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// What a dataset-level axis-order check concluded about a dataset's
/// positions (issue #650).
/// </summary>
public enum AxisOrderOutcome
{
    /// <summary>
    /// The positions fit the declared extent as encoded: latitude first, as
    /// the specification requires.
    /// </summary>
    AsEncoded,

    /// <summary>
    /// The positions are encoded longitude first. Readers swap every
    /// position of the dataset back to latitude, longitude.
    /// </summary>
    Swapped,

    /// <summary>
    /// The dataset declares no usable extent and its positions are within
    /// physical bounds, so the order cannot be checked. Positions are kept as
    /// encoded.
    /// </summary>
    Unverified,

    /// <summary>
    /// The dataset declares an extent, but its positions fit it in neither
    /// order (or the positions are out of physical bounds either way).
    /// Positions are kept as encoded.
    /// </summary>
    Inconsistent,
}

/// <summary>
/// The anchor an axis-order check based its decision on.
/// </summary>
public enum AxisOrderEvidence
{
    /// <summary>No anchor was available.</summary>
    None,

    /// <summary>
    /// A latitude beyond ±90°, which can only be a longitude.
    /// </summary>
    PhysicalBounds,

    /// <summary>
    /// The extent the dataset declares (an HDF5 bounding-box attribute set
    /// or the GML <c>gml:Envelope</c>).
    /// </summary>
    DeclaredExtent,
}

/// <summary>
/// The result of <see cref="AxisOrderCheck.Assess"/>: whether a dataset's
/// positions are latitude first, the evidence behind the decision, and the
/// counts that support it. Readers attach it to the dataset model so hosts can
/// report it (<c>s100 info</c>, validation findings, the viewer's dataset
/// list).
/// </summary>
public sealed record AxisOrderAssessment
{
    /// <summary>The decision.</summary>
    public required AxisOrderOutcome Outcome { get; init; }

    /// <summary>The anchor the decision rests on.</summary>
    public required AxisOrderEvidence Evidence { get; init; }

    /// <summary>The number of positions checked.</summary>
    public required int PositionCount { get; init; }

    /// <summary>
    /// The number of positions inside the declared extent as encoded, or 0
    /// when there is no declared extent.
    /// </summary>
    public int FitAsEncoded { get; init; }

    /// <summary>
    /// The number of positions inside the declared extent with latitude and
    /// longitude swapped, or 0 when there is no declared extent.
    /// </summary>
    public int FitSwapped { get; init; }

    /// <summary>
    /// The number of positions whose encoded latitude is beyond ±90°.
    /// </summary>
    public int OutOfPhysicalBounds { get; init; }

    /// <summary>
    /// Whether the positions fit the declared extent only swapped, but the
    /// extent is an ordinal GML envelope too loose around them to say
    /// whether the positions or the envelope are the swapped ones. The
    /// outcome is then <see cref="AxisOrderOutcome.Inconsistent"/>.
    /// </summary>
    public bool ExtentMayBeSwapped { get; init; }

    /// <summary>The declared extent used as the anchor, if any.</summary>
    public BoundingBox? DeclaredExtent { get; init; }

    /// <summary>
    /// What the positions were checked from, for messages (for example
    /// <c>"/WaterLevel/WaterLevel.01/Positioning/geometryValues"</c> or
    /// <c>"gml:pos/gml:posList"</c>).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Whether the reader swapped the positions. True exactly when
    /// <see cref="Outcome"/> is <see cref="AxisOrderOutcome.Swapped"/> and
    /// the reader normalises; a reader that only reports (for example for
    /// gridded coverages, whose georeferencing it does not rewrite) leaves
    /// this false.
    /// </summary>
    public bool Normalized { get; init; }

    /// <summary>
    /// Whether the outcome deserves a notice: anything other than
    /// <see cref="AxisOrderOutcome.AsEncoded"/>.
    /// </summary>
    public bool IsNotice => Outcome != AxisOrderOutcome.AsEncoded;

    /// <summary>
    /// Whether the outcome is a warning: the positions were swapped, or do
    /// not fit the declared extent. <see cref="AxisOrderOutcome.Unverified"/>
    /// is a notice but not a warning.
    /// </summary>
    public bool IsWarning => Outcome is AxisOrderOutcome.Swapped or AxisOrderOutcome.Inconsistent;

    /// <summary>
    /// Builds a one-sentence, human-readable description of the outcome.
    /// </summary>
    public string BuildMessage()
    {
        var where = Source is null ? "Positions" : $"Positions in {Source}";
        var extent = DeclaredExtent is { } e
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"declared extent (lat {e.SouthLatitude:0.###}..{e.NorthLatitude:0.###}, lon {e.WestLongitude:0.###}..{e.EastLongitude:0.###})")
            : "declared extent";

        return Outcome switch
        {
            AxisOrderOutcome.AsEncoded => Evidence == AxisOrderEvidence.DeclaredExtent
                ? $"{where} fit the {extent} latitude first ({FitAsEncoded} of {PositionCount})."
                : $"{where} are latitude first.",
            AxisOrderOutcome.Swapped => Evidence == AxisOrderEvidence.PhysicalBounds
                ? $"{where} are encoded longitude first: {OutOfPhysicalBounds} of {PositionCount} have a latitude beyond ±90°. "
                    + (Normalized ? "Latitude and longitude were swapped for the whole dataset." : "They were not swapped.")
                : $"{where} are encoded longitude first: {FitSwapped} of {PositionCount} fit the {extent} only with latitude and longitude swapped. "
                    + (Normalized ? "Latitude and longitude were swapped for the whole dataset." : "They were not swapped."),
            AxisOrderOutcome.Unverified =>
                $"Axis order of {PositionCount} positions could not be verified: the dataset declares no extent. Latitude first is assumed.",
            AxisOrderOutcome.Inconsistent when ExtentMayBeSwapped =>
                $"{where} fit the {extent} only with latitude and longitude swapped ({FitSwapped} of {PositionCount}), but the envelope is too loose around them to tell whether the positions or the envelope are longitude first. They were kept as encoded.",
            AxisOrderOutcome.Inconsistent => OutOfPhysicalBounds > 0
                ? $"{where} are out of physical bounds in either axis order ({OutOfPhysicalBounds} of {PositionCount} have a latitude beyond ±90°). They were kept as encoded."
                : $"{where} fit the {extent} in neither axis order ({FitAsEncoded} as encoded, {FitSwapped} swapped, of {PositionCount}). They were kept as encoded.",
            _ => where,
        };
    }
}
