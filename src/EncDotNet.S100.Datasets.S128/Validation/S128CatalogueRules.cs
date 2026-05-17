using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S128.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S128.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-128 <see cref="S128ProductCatalogue"/>. Rule identifiers follow
/// the convention <c>S128-R-{clause}</c>, where <c>{clause}</c> traces to
/// the relevant section of the S-128 (Edition 2.0.0) specification.
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S128ProductCatalogue"/> in isolation. Tier-3 cross-dataset
/// rules (e.g. comparing a catalogue's product references against actually
/// loaded S-1xx datasets) will be added in a follow-up once the MCP
/// <c>validate_all</c> surface is wired up — they need access to sibling
/// datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// </remarks>
public static class S128CatalogueRules
{
    /// <summary>
    /// <c>S128-R-12.1</c> — When present, every catalogue entry's
    /// <c>editionNumber</c> must be ≥ 1.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-128 § 12 — <c>editionNumber</c> is the
    /// author-assigned edition counter shared across
    /// <c>ElectronicProduct</c>, <c>PhysicalProduct</c>, and
    /// <c>S100Service</c>. The S-100 lifecycle treats edition 1 as the
    /// first publishable edition; values of 0 or below are not meaningful.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> EditionNumberPositive { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.1")
            .WithDescription("When present, product editionNumber must be ≥ 1.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((catalogue, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var entry in catalogue.Products)
                {
                    if (entry.EditionNumber is not { } edition || edition >= 1)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S128-R-12.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Product '{entry.Id}' ({entry.FeatureType}) has editionNumber " +
                            $"{edition}; must be ≥ 1.",
                        DatasetId = catalogue.DatasetIdentifier,
                        RelatedFeatureId = entry.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.2</c> — When both an <c>issueDate</c> and an
    /// <c>updateDate</c> are present on a catalogue entry, the issue date
    /// must not be later than the update date.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-128 § 12 — <c>issueDate</c> records the original
    /// release date of an edition; <c>updateDate</c> (falling back to
    /// <c>editionDate</c>) records the most recent revision date for that
    /// edition. An update cannot precede its own issue.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> IssueDateBeforeUpdateDate { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.2")
            .WithDescription("issueDate must not be later than updateDate when both are present.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((catalogue, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var entry in catalogue.Products)
                {
                    if (entry.IssueDate is not { } issued || entry.UpdateDate is not { } updated)
                        continue;
                    if (issued <= updated)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S128-R-12.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Product '{entry.Id}' issueDate ({issued:O}) is later than " +
                            $"updateDate ({updated:O}).",
                        DatasetId = catalogue.DatasetIdentifier,
                        RelatedFeatureId = entry.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.3</c> — Every coordinate on every catalogue entry must
    /// lie within the WGS-84 ranges: latitude in [-90, +90] and longitude
    /// in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. S-128 catalogue entries carry
    /// coverage geometry as a point, curve, or surface exterior ring.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.3")
            .WithDescription("Coverage coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((catalogue, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var entry in catalogue.Products)
                {
                    if (entry.Coordinates.IsDefaultOrEmpty)
                        continue;

                    for (int i = 0; i < entry.Coordinates.Length; i++)
                    {
                        var pos = entry.Coordinates[i];
                        bool latOk = pos.Latitude is >= -90 and <= 90;
                        bool lonOk = pos.Longitude is >= -180 and <= 180;
                        if (latOk && lonOk) continue;

                        var details = (latOk, lonOk) switch
                        {
                            (false, true) => $"latitude {pos.Latitude} is outside [-90, +90]",
                            (true, false) => $"longitude {pos.Longitude} is outside [-180, +180]",
                            _ => $"latitude {pos.Latitude} and longitude {pos.Longitude} are both out of range",
                        };
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S128-R-12.3",
                            Severity = ValidationSeverity.Error,
                            Message = $"Product '{entry.Id}' coordinate [{i}]: {details}.",
                            Point = pos,
                            DatasetId = catalogue.DatasetIdentifier,
                            RelatedFeatureId = entry.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.4</c> — When a catalogue entry carries a
    /// <see cref="S128GeometryKind.Surface"/> geometry, its exterior ring
    /// must contain at least four vertices and its first and last vertex
    /// must coincide (closed ring).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6 (surface primitives) — a GML
    /// polygon's exterior ring is a closed <c>LinearRing</c>. The minimum
    /// of four positions follows from "three distinct corners + repeat the
    /// first to close". Coincidence is tested with a tight tolerance
    /// (1e-9 degrees, about 0.1 mm at the equator) so that ordinary
    /// floating-point round-trip does not trigger the rule.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> SurfaceRingClosed { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.4")
            .WithDescription("Surface exterior rings must have ≥4 vertices and be closed.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((catalogue, _) =>
            {
                const double tolerance = 1e-9;
                var findings = new List<ValidationFinding>();
                foreach (var entry in catalogue.Products)
                {
                    if (entry.GeometryKind != S128GeometryKind.Surface)
                        continue;

                    var ring = entry.Coordinates;
                    if (ring.IsDefaultOrEmpty || ring.Length < 4)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S128-R-12.4",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Product '{entry.Id}' surface ring has " +
                                $"{(ring.IsDefault ? 0 : ring.Length)} vertices; ≥4 required.",
                            DatasetId = catalogue.DatasetIdentifier,
                            RelatedFeatureId = entry.Id,
                        });
                        continue;
                    }

                    var first = ring[0];
                    var last = ring[^1];
                    if (Math.Abs(first.Latitude - last.Latitude) > tolerance
                        || Math.Abs(first.Longitude - last.Longitude) > tolerance)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S128-R-12.4",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Product '{entry.Id}' surface ring is not closed: first " +
                                $"({first.Latitude}, {first.Longitude}) != last " +
                                $"({last.Latitude}, {last.Longitude}).",
                            Point = first,
                            DatasetId = catalogue.DatasetIdentifier,
                            RelatedFeatureId = entry.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.5</c> — Every catalogue product must have a unique
    /// <see cref="S128CatalogueEntry.Id"/>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §10 (GML identity) and S-128 § 12 —
    /// the <c>gml:id</c> on a feature instance is the primary key used by
    /// xlinks (e.g. <c>theReference</c> supersedes pointers) to address
    /// catalogue entries. Duplicate identifiers break xlink resolution and
    /// supersedes navigation.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> UniqueProductIds { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.5")
            .WithDescription("Catalogue product gml:id values must be unique.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((catalogue, _) =>
            {
                var findings = new List<ValidationFinding>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in catalogue.Products)
                {
                    if (!seen.Add(entry.Id))
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S128-R-12.5",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Duplicate product gml:id '{entry.Id}' " +
                                $"({entry.FeatureType}).",
                            DatasetId = catalogue.DatasetIdentifier,
                            RelatedFeatureId = entry.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.6</c> — When an <c>onlineResource/linkage</c> is
    /// supplied on a catalogue entry, it must parse as an absolute URI.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-128 § 12 <c>onlineResource</c> complex attribute
    /// — <c>linkage</c> carries the URL at which the product may be
    /// downloaded or accessed. A relative or malformed value cannot be
    /// dereferenced by clients.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> OnlineResourceLinkageWellFormed { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.6")
            .WithDescription("onlineResource/linkage values must be absolute URIs when present.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((catalogue, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var entry in catalogue.Products)
                {
                    if (entry.OnlineResources.IsDefaultOrEmpty)
                        continue;

                    for (int i = 0; i < entry.OnlineResources.Length; i++)
                    {
                        var linkage = entry.OnlineResources[i].Linkage;
                        if (string.IsNullOrWhiteSpace(linkage))
                            continue;

                        if (!Uri.TryCreate(linkage, UriKind.Absolute, out Uri? _))
                        {
                            findings.Add(new ValidationFinding
                            {
                                RuleId = "S128-R-12.6",
                                Severity = ValidationSeverity.Warning,
                                Message =
                                    $"Product '{entry.Id}' onlineResource[{i}].linkage " +
                                    $"'{linkage}' is not an absolute URI.",
                                DatasetId = catalogue.DatasetIdentifier,
                                RelatedFeatureId = entry.Id,
                            });
                        }
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S128-R-12.7</c> — A catalogue dataset should carry at least one
    /// producer or distributor metadata record.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-128 § 12 — a Catalogue of Nautical Products is
    /// always published by an agency, and the encoded catalogue is
    /// expected to identify either the producing agency
    /// (<c>ProducerInformation</c>) or at least one distributor
    /// (<c>DistributorInformation</c>). A catalogue with neither cannot
    /// be attributed and is treated as a soft warning rather than a
    /// hard error so legacy / partial fixtures still parse.
    /// </remarks>
    public static IValidationRule<S128ProductCatalogue> ProducerOrDistributorPresent { get; } =
        ValidationRuleBuilder.RuleFor<S128ProductCatalogue>("S128-R-12.7")
            .WithDescription("Catalogue must declare at least one producer or distributor.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((catalogue, _) =>
            {
                bool hasProducer = !catalogue.Producers.IsDefaultOrEmpty
                    && catalogue.Producers.Length > 0;
                bool hasDistributor = !catalogue.Distributors.IsDefaultOrEmpty
                    && catalogue.Distributors.Length > 0;
                if (hasProducer || hasDistributor)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S128-R-12.7",
                        Severity = ValidationSeverity.Warning,
                        Message = "Catalogue carries no ProducerInformation or DistributorInformation record.",
                        DatasetId = catalogue.DatasetIdentifier,
                    },
                };
            })
            .Build();

    /// <summary>The canonical default rule set for S-128 catalogues.</summary>
    public static ValidationRuleSet<S128ProductCatalogue> Default { get; } = new(
        EditionNumberPositive,
        IssueDateBeforeUpdateDate,
        CoordinatesInWgs84Range,
        SurfaceRingClosed,
        UniqueProductIds,
        OnlineResourceLinkageWellFormed,
        ProducerOrDistributorPresent);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S128ProductCatalogue catalogue, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return Default.Run(catalogue, context);
    }
}
