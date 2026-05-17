using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S124.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-124 <see cref="S124NavigationalWarning"/>. Rule identifiers
/// follow the convention <c>S124-R-{clause}</c>, where <c>{clause}</c>
/// traces to the relevant section of the S-124 (Edition 1.0.0) Feature
/// Catalogue or to S-100 Part 10b for shared encoding constraints.
/// </summary>
/// <remarks>
/// <para>
/// This pack covers Tier-1 (schema-shape) and Tier-2 (spec-semantic)
/// rules that can be evaluated against a single
/// <see cref="S124NavigationalWarning"/> in isolation. Tier-3
/// cross-dataset rules (e.g. resolving the referenced warning of a
/// cancellation against a sibling catalogue) are out of scope; they need
/// the MCP <c>validate_all</c> surface and access to sibling datasets
/// via <see cref="ValidationContext.Services"/>.
/// </para>
/// <para>
/// Rules read from the strongly-typed projection produced by
/// <see cref="S124NavigationalWarning.From(S124Dataset, out IReadOnlyList{ProjectionDiagnostic})"/>;
/// projection diagnostics (duplicate preamble, unresolved xlinks,
/// unparseable values) are surfaced separately and are not duplicated
/// here.
/// </para>
/// </remarks>
public static class S124NavigationalWarningRules
{
    /// <summary>
    /// The set of NAVAREA codes recognised by the IHO/IMO World Wide
    /// Navigational Warning Service (NAVAREAs I–XXI). The S-124
    /// Feature Catalogue restricts the <c>NAVAREA</c> attribute on
    /// <c>NavwarnPreamble</c> to this enumerated set.
    /// </summary>
    private static readonly ImmutableHashSet<string> NavareaCodes = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
        "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX", "XXI");

    /// <summary>
    /// <c>S124-R-1.1</c> — A navigational warning must contain at least
    /// one <c>NavwarnPart</c> feature.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue — a
    /// <c>NavwarnPreamble</c> aggregates one or more <c>NavwarnPart</c>
    /// features via the <c>header</c> association. A dataset that
    /// projects with zero parts has no substantive warning content.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> MinimumPartCount { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-1.1")
            .WithDescription("A navigational warning must contain at least one NavwarnPart.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                if (warning.Parts.Length > 0)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S124-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = "Navigational warning contains no NavwarnPart features.",
                        DatasetId = warning.DatasetIdentifier,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S124-R-2.1</c> — Every navigational warning must include a
    /// <c>NavwarnPreamble</c> information type.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue —
    /// <c>NavwarnPreamble</c> is mandatory; it carries the
    /// identification, classification, locality, and promulgating
    /// authority of the warning.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> PreambleRequired { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-2.1")
            .WithDescription("Every navigational warning must include a NavwarnPreamble.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                if (warning.Preamble is not null)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S124-R-2.1",
                        Severity = ValidationSeverity.Error,
                        Message = "Navigational warning has no NavwarnPreamble information type.",
                        DatasetId = warning.DatasetIdentifier,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S124-R-2.2</c> — When a <c>messageSeriesIdentifier</c> is
    /// present on the preamble, the warning number must be ≥ 1 and the
    /// year must be a plausible four-digit calendar year
    /// (1900 ≤ year ≤ 2100).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue —
    /// <c>messageSeriesIdentifier</c> sub-attributes <c>warningNumber</c>
    /// (sequential, monotonically increasing within the series) and
    /// <c>year</c> (the issuance calendar year). Zero or negative
    /// warning numbers, and years outside the plausible publication
    /// range, indicate authoring errors.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> MessageSeriesIdentifierWellFormed { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-2.2")
            .WithDescription("messageSeriesIdentifier must carry a positive warning number and a plausible year.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                var msi = warning.Preamble?.MessageSeriesIdentifier;
                if (msi is null)
                    return Array.Empty<ValidationFinding>();

                var findings = new List<ValidationFinding>();
                if (msi.WarningNumber is { } wn && wn < 1)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S124-R-2.2",
                        Severity = ValidationSeverity.Error,
                        Message = $"messageSeriesIdentifier.warningNumber must be ≥ 1, found {wn}.",
                        DatasetId = warning.DatasetIdentifier,
                        RelatedFeatureId = warning.Preamble!.Id,
                    });
                }

                if (msi.Year is { } y && (y < 1900 || y > 2100))
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S124-R-2.2",
                        Severity = ValidationSeverity.Error,
                        Message = $"messageSeriesIdentifier.year {y} is outside the plausible range [1900, 2100].",
                        DatasetId = warning.DatasetIdentifier,
                        RelatedFeatureId = warning.Preamble!.Id,
                    });
                }

                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S124-R-3.1</c> — When <c>NAVAREA</c> is present on the
    /// preamble, its value must be one of the IMO-recognised NAVAREA
    /// Roman-numeral codes (I…XXI).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue, attribute
    /// <c>NAVAREA</c> on <c>NavwarnPreamble</c>; codelist matches the
    /// IMO World Wide Navigational Warning Service NAVAREA scheme.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> NavareaCodeValid { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-3.1")
            .WithDescription("NAVAREA must be one of the IMO NAVAREA codes I…XXI.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                var code = warning.Preamble?.NavareaCode;
                if (string.IsNullOrWhiteSpace(code) || NavareaCodes.Contains(code.Trim()))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S124-R-3.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"NAVAREA code '{code}' is not one of the recognised IMO NAVAREAs (I…XXI).",
                        DatasetId = warning.DatasetIdentifier,
                        RelatedFeatureId = warning.Preamble!.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S124-R-4.1</c> — Every coordinate on every part, affected
    /// area, and text placement position must lie within the WGS-84
    /// bounds: latitude in [-90, +90] and longitude in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> CoordinatesInRange { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-4.1")
            .WithDescription("All coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                var findings = new List<ValidationFinding>();

                foreach (var part in warning.Parts)
                {
                    EmitOutOfRange(findings, warning.DatasetIdentifier, part.Id, "NavwarnPart", part.Coordinates);
                    foreach (var area in part.AffectedAreas)
                        EmitOutOfRange(findings, warning.DatasetIdentifier, area.Id, "NavwarnAreaAffected", area.Coordinates);
                    foreach (var tp in part.TextPlacements)
                    {
                        if (tp.Position is { } pos)
                            EmitOutOfRange(findings, warning.DatasetIdentifier, tp.Id, "TextPlacement",
                                ImmutableArray.Create(pos));
                    }
                }

                return findings;

                static void EmitOutOfRange(
                    List<ValidationFinding> sink,
                    string? datasetId,
                    string featureId,
                    string featureType,
                    ImmutableArray<GeoPosition> coords)
                {
                    if (coords.IsDefaultOrEmpty) return;
                    for (int i = 0; i < coords.Length; i++)
                    {
                        var pos = coords[i];
                        bool latOk = pos.Latitude is >= -90 and <= 90;
                        bool lonOk = pos.Longitude is >= -180 and <= 180;
                        if (latOk && lonOk) continue;

                        var details = (latOk, lonOk) switch
                        {
                            (false, true) => $"latitude {pos.Latitude} is outside [-90, +90]",
                            (true, false) => $"longitude {pos.Longitude} is outside [-180, +180]",
                            _ => $"latitude {pos.Latitude} and longitude {pos.Longitude} are both out of range",
                        };
                        sink.Add(new ValidationFinding
                        {
                            RuleId = "S124-R-4.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"{featureType} '{featureId}' coordinate [{i}]: {details}.",
                            Point = pos,
                            DatasetId = datasetId,
                            RelatedFeatureId = featureId,
                        });
                    }
                }
            })
            .Build();

    /// <summary>
    /// <c>S124-R-4.2</c> — Surface geometries must be closed: the first
    /// and last coordinate of the exterior ring must coincide, and the
    /// ring must contain at least four positions.
    /// </summary>
    /// <remarks>
    /// Spec reference: ISO 19136 / GML 3.2 §10.5.1 (<c>gml:LinearRing</c>)
    /// and S-100 Part 10b §6.4 — a polygon's exterior ring is a closed
    /// curve. A closed ring requires the start and end position to be
    /// equal and a minimum of four positions (three distinct vertices
    /// plus the closing copy).
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> SurfaceRingClosed { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-4.2")
            .WithDescription("Surface exterior rings must be closed and contain at least four positions.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                const double tolerance = 1e-9;
                var findings = new List<ValidationFinding>();

                foreach (var part in warning.Parts)
                {
                    if (part.GeometryKind == S124GeometryKind.Surface)
                        Check(findings, warning.DatasetIdentifier, part.Id, "NavwarnPart", part.Coordinates);
                    foreach (var area in part.AffectedAreas)
                    {
                        if (area.GeometryKind == S124GeometryKind.Surface)
                            Check(findings, warning.DatasetIdentifier, area.Id, "NavwarnAreaAffected", area.Coordinates);
                    }
                }

                return findings;

                static void Check(
                    List<ValidationFinding> sink,
                    string? datasetId,
                    string featureId,
                    string featureType,
                    ImmutableArray<GeoPosition> coords)
                {
                    if (coords.IsDefaultOrEmpty || coords.Length < 4)
                    {
                        sink.Add(new ValidationFinding
                        {
                            RuleId = "S124-R-4.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"{featureType} '{featureId}' surface ring has " +
                                $"{(coords.IsDefaultOrEmpty ? 0 : coords.Length)} position(s); a closed ring requires at least four.",
                            DatasetId = datasetId,
                            RelatedFeatureId = featureId,
                        });
                        return;
                    }

                    var first = coords[0];
                    var last = coords[^1];
                    if (Math.Abs(first.Latitude - last.Latitude) > tolerance
                        || Math.Abs(first.Longitude - last.Longitude) > tolerance)
                    {
                        sink.Add(new ValidationFinding
                        {
                            RuleId = "S124-R-4.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"{featureType} '{featureId}' surface ring is not closed: " +
                                $"first ({first.Latitude}, {first.Longitude}) ≠ last ({last.Latitude}, {last.Longitude}).",
                            Point = first,
                            DatasetId = datasetId,
                            RelatedFeatureId = featureId,
                        });
                    }
                }
            })
            .Build();

    /// <summary>
    /// <c>S124-R-5.1</c> — Each <c>NavwarnPart</c> must convey
    /// substantive content via a non-empty <c>warningInformation</c>
    /// text or at least one non-empty associated <c>TextPlacement</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue —
    /// <c>NavwarnPart</c> exists to convey a navigational message; a
    /// part with no text payload (neither <c>warningInformation</c>
    /// nor an associated <c>TextPlacement</c>) is operationally
    /// meaningless and almost certainly an authoring error.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> PartHasWarningText { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-5.1")
            .WithDescription("Each NavwarnPart must carry non-empty warning text.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((warning, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var part in warning.Parts)
                {
                    if (!string.IsNullOrWhiteSpace(part.WarningInformation))
                        continue;
                    if (part.TextPlacements.Any(tp => !string.IsNullOrWhiteSpace(tp.Text)))
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S124-R-5.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"NavwarnPart '{part.Id}' has neither warningInformation text nor a non-empty " +
                            "associated TextPlacement.",
                        DatasetId = warning.DatasetIdentifier,
                        RelatedFeatureId = part.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S124-R-6.1</c> — Every <c>References</c> information type
    /// that sets <c>referenceCategory</c> must also supply a non-empty
    /// <c>messageReference</c> identifying the warning being
    /// referenced.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-124 Edition 1.0.0 Feature Catalogue,
    /// information type <c>References</c>. A reference category
    /// (cancellation, supersession, …) without a target message
    /// identifier cannot be acted on by ECDIS or any downstream
    /// consumer.
    /// </remarks>
    public static IValidationRule<S124NavigationalWarning> ReferenceTargetSpecified { get; } =
        ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-6.1")
            .WithDescription("A References information type that sets referenceCategory must include messageReference.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((warning, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var r in warning.References)
                {
                    if (r.ReferenceCategory is null)
                        continue;
                    if (!string.IsNullOrWhiteSpace(r.MessageReference))
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S124-R-6.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"References '{r.Id}' sets referenceCategory={r.ReferenceCategory} but has no " +
                            "messageReference identifying the target warning.",
                        DatasetId = warning.DatasetIdentifier,
                        RelatedFeatureId = r.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-124 navigational warnings.</summary>
    public static ValidationRuleSet<S124NavigationalWarning> Default { get; } = new(
        MinimumPartCount,
        PreambleRequired,
        MessageSeriesIdentifierWellFormed,
        NavareaCodeValid,
        CoordinatesInRange,
        SurfaceRingClosed,
        PartHasWarningText,
        ReferenceTargetSpecified);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S124NavigationalWarning warning, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return Default.Run(warning, context);
    }
}
