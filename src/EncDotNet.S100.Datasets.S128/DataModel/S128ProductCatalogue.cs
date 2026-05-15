using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S128.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S128Dataset"/> as a catalogue
/// of nautical products (S-128 Edition 2.0.0).
/// </summary>
/// <remarks>
/// <para>
/// S-128 datasets describe the nautical products produced by an agency.
/// The typed model surfaces three product subclasses
/// (<see cref="S128ElectronicProduct"/>, <see cref="S128PhysicalProduct"/>,
/// <see cref="S128Service"/>) through a common
/// <see cref="S128CatalogueEntry"/> base, plus dedicated typed records
/// for producer, distributor, contact, and section-header metadata.
/// </para>
/// <para>
/// The headline value-add is supersedes resolution: every product
/// entry's <see cref="S128CatalogueEntry.Supersedes"/> and
/// <see cref="S128CatalogueEntry.SupersededBy"/> collections are
/// populated from the dataset's <c>theReference</c> xlinks whose
/// companion <c>ProductMapping/categoryOfProductMapping</c> classifies
/// as <see cref="S128ProductMappingCategory.HigherPriorityAlternative"/>
/// (S-128 § 12). All other mappings land in
/// <see cref="S128CatalogueEntry.RelatedProducts"/> with their raw
/// category text preserved for forward compatibility.
/// </para>
/// <para>
/// Projection issues — unresolved xlinks, attribute parse failures —
/// surface as <see cref="ProjectionDiagnostic"/> entries rather than
/// exceptions. The projection only throws when the source dataset is
/// fully empty (no features and no information types).
/// </para>
/// </remarks>
public sealed class S128ProductCatalogue
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-128 product identifier (typically <c>"S-128"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>All catalogue product entries, polymorphic over the three product subclasses.</summary>
    public required ImmutableArray<S128CatalogueEntry> Products { get; init; }

    /// <summary>Producer metadata records.</summary>
    public ImmutableArray<S128ProducerInformation> Producers { get; init; } =
        ImmutableArray<S128ProducerInformation>.Empty;

    /// <summary>Distributor metadata records.</summary>
    public ImmutableArray<S128DistributorInformation> Distributors { get; init; } =
        ImmutableArray<S128DistributorInformation>.Empty;

    /// <summary>Contact-details metadata records.</summary>
    public ImmutableArray<S128ContactDetails> Contacts { get; init; } =
        ImmutableArray<S128ContactDetails>.Empty;

    /// <summary>Catalogue section header records.</summary>
    public ImmutableArray<S128CatalogueSectionHeader> SectionHeaders { get; init; } =
        ImmutableArray<S128CatalogueSectionHeader>.Empty;

    /// <summary>The originating feature-bag dataset.</summary>
    public required S128Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S128Dataset"/> into the typed data
    /// model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection only throws for a
    /// fully empty dataset (no features and no information types).
    /// </summary>
    /// <param name="dataset">The source dataset.</param>
    /// <param name="diagnostics">Out parameter receiving the projection diagnostics.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="dataset"/> contains neither features nor
    /// information types.
    /// </exception>
    public static S128ProductCatalogue From(S128Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Features.IsDefaultOrEmpty && dataset.InformationTypes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dataset contains no features and no information types.");

        // First pass: build typed entries (without cross-references yet).
        var productsBuilder = ImmutableArray.CreateBuilder<S128CatalogueEntry>();
        var producersBuilder = ImmutableArray.CreateBuilder<S128ProducerInformation>();
        var distributorsBuilder = ImmutableArray.CreateBuilder<S128DistributorInformation>();
        var contactsBuilder = ImmutableArray.CreateBuilder<S128ContactDetails>();
        var headersBuilder = ImmutableArray.CreateBuilder<S128CatalogueSectionHeader>();

        // The xlink resolver and diagnostics get filled during pass 2, so
        // build the context up front so we can hand it to the per-feature
        // helpers below.
        var ctx = new ProjectionContext(XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>()));

        foreach (var f in dataset.Features)
        {
            switch (f.FeatureType)
            {
                case var t when t.Equals("ElectronicProduct", StringComparison.OrdinalIgnoreCase):
                    productsBuilder.Add(ProjectElectronicProduct(f, ctx));
                    break;
                case var t when t.Equals("PhysicalProduct", StringComparison.OrdinalIgnoreCase):
                    productsBuilder.Add(ProjectPhysicalProduct(f, ctx));
                    break;
                case var t when t.Equals("S100Service", StringComparison.OrdinalIgnoreCase):
                    productsBuilder.Add(ProjectService(f, ctx));
                    break;
                case var t when t.Equals("ProducerInformation", StringComparison.OrdinalIgnoreCase):
                    producersBuilder.Add(ProjectProducer(f));
                    break;
                case var t when t.Equals("DistributorInformation", StringComparison.OrdinalIgnoreCase):
                    distributorsBuilder.Add(ProjectDistributor(f));
                    break;
                case var t when t.Equals("ContactDetails", StringComparison.OrdinalIgnoreCase):
                    contactsBuilder.Add(ProjectContact(f));
                    break;
                case var t when t.Equals("CatalogueSectionHeader", StringComparison.OrdinalIgnoreCase):
                    headersBuilder.Add(ProjectSectionHeader(f));
                    break;
                default:
                    // Unknown feature type: silently skip — the catalogue
                    // model is closed; producer extensions remain visible
                    // on dataset.Features.
                    break;
            }
        }

        var products = productsBuilder.ToImmutable();

        // Second pass: build a real xlink resolver that knows about every
        // entry, then resolve theReference xlinks and populate the
        // forward Supersedes / RelatedProducts collections.
        var resolverEntries = new List<KeyValuePair<string, object>>(
            products.Length
            + producersBuilder.Count
            + distributorsBuilder.Count
            + contactsBuilder.Count
            + headersBuilder.Count);
        foreach (var p in products) resolverEntries.Add(new(p.Id, p));
        foreach (var p in producersBuilder) resolverEntries.Add(new(p.Id, p));
        foreach (var d in distributorsBuilder) resolverEntries.Add(new(d.Id, d));
        foreach (var c in contactsBuilder) resolverEntries.Add(new(c.Id, c));
        foreach (var h in headersBuilder) resolverEntries.Add(new(h.Id, h));

        // Re-create the context with the real resolver, preserving any
        // diagnostics already collected during pass 1.
        var resolver = XlinkResolver.Build(resolverEntries);
        var ctx2 = new ProjectionContext(resolver);
        foreach (var d in ctx.Diagnostics) ctx2.Report(d);

        // Resolve theReference cross-references on each product entry.
        // Forward map: referrer.Id → list of resolved targets to supersede.
        var supersedesByReferrer = new Dictionary<string, List<S128CatalogueEntry>>(StringComparer.OrdinalIgnoreCase);
        var relatedByReferrer = new Dictionary<string, List<S128ProductReference>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in products)
        {
            ResolveProductReferences(entry, ctx2, supersedesByReferrer, relatedByReferrer);
        }

        // Mutate Supersedes / RelatedProducts on each entry.
        foreach (var entry in products)
        {
            if (supersedesByReferrer.TryGetValue(entry.Id, out var supTargets))
                entry.Supersedes = supTargets.ToImmutableArray();

            if (relatedByReferrer.TryGetValue(entry.Id, out var rel))
            {
                entry.RelatedProducts = rel.ToImmutableArray();
            }
        }

        // Reverse map: target.Id → list of referrers that supersede it.
        var supersededByMap = new Dictionary<string, List<S128CatalogueEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (referrerId, targets) in supersedesByReferrer)
        {
            // Find the referrer entry.
            S128CatalogueEntry? referrer = null;
            foreach (var p in products)
            {
                if (string.Equals(p.Id, referrerId, StringComparison.OrdinalIgnoreCase))
                {
                    referrer = p;
                    break;
                }
            }
            if (referrer is null) continue;

            foreach (var target in targets)
            {
                if (!supersededByMap.TryGetValue(target.Id, out var list))
                    supersededByMap[target.Id] = list = new List<S128CatalogueEntry>();
                list.Add(referrer);
            }
        }
        foreach (var entry in products)
        {
            if (supersededByMap.TryGetValue(entry.Id, out var referrers))
                entry.SupersededBy = referrers.ToImmutableArray();
        }

        diagnostics = ctx2.ToImmutableDiagnostics();
        return new S128ProductCatalogue
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Products = products,
            Producers = producersBuilder.ToImmutable(),
            Distributors = distributorsBuilder.ToImmutable(),
            Contacts = contactsBuilder.ToImmutable(),
            SectionHeaders = headersBuilder.ToImmutable(),
            Source = dataset,
        };
    }

    // ── Per-feature projection ─────────────────────────────────────────

    private readonly record struct CommonFields(
        string? ProductNumber,
        int? EditionNumber,
        int? UpdateNumber,
        DateTimeOffset? IssueDate,
        DateTimeOffset? UpdateDate,
        string? Classification,
        bool? NotForNavigation,
        string? ProductSpecificationName,
        string? ProductSpecificationVersion,
        S128GeometryKind GeometryKind,
        ImmutableArray<GeoPosition> Coordinates,
        ImmutableArray<S128OnlineResource> OnlineResources,
        ImmutableDictionary<string, string> ExtraAttributes);

    private static CommonFields ExtractCommon(S128Feature f, ProjectionContext ctx)
    {
        S128ComplexAttribute? ps = null;
        foreach (var c in f.ComplexAttributes)
        {
            if (c.Code.Equals("productSpecification", StringComparison.OrdinalIgnoreCase))
            {
                ps = c;
                break;
            }
        }

        var (kind, coords) = ProjectGeometry(f);
        var online = ProjectOnlineResources(f);

        bool? notForNav = null;
        if (f.Attributes.TryGetValue("notForNavigation", out var nfn))
            notForNav = AttributeParser.TryParseBool(nfn, ctx, f.Id, "notForNavigation");

        return new CommonFields(
            ProductNumber:
                f.Attributes.GetValueOrDefault("productNumber") ??
                f.Attributes.GetValueOrDefault("datasetName"),
            EditionNumber: AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault("editionNumber"), ctx, f.Id, "editionNumber"),
            UpdateNumber: AttributeParser.TryParseInt(
                f.Attributes.GetValueOrDefault("updateNumber"), ctx, f.Id, "updateNumber"),
            IssueDate: AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("issueDate"), ctx, f.Id, "issueDate"),
            UpdateDate: AttributeParser.TryParseDateTimeOffset(
                f.Attributes.GetValueOrDefault("updateDate")
                    ?? f.Attributes.GetValueOrDefault("editionDate"),
                ctx, f.Id, "updateDate"),
            Classification: f.Attributes.GetValueOrDefault("catalogueElementClassification"),
            NotForNavigation: notForNav,
            ProductSpecificationName: ps?.SubAttributes.GetValueOrDefault("name"),
            ProductSpecificationVersion: ps?.SubAttributes.GetValueOrDefault("version"),
            GeometryKind: kind,
            Coordinates: coords,
            OnlineResources: online,
            ExtraAttributes: BuildExtraAttributes(f));
    }

    private static S128ElectronicProduct ProjectElectronicProduct(S128Feature f, ProjectionContext ctx)
    {
        var c = ExtractCommon(f, ctx);
        return new S128ElectronicProduct
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            Source = f,
            ProductNumber = c.ProductNumber,
            EditionNumber = c.EditionNumber,
            UpdateNumber = c.UpdateNumber,
            IssueDate = c.IssueDate,
            UpdateDate = c.UpdateDate,
            Classification = c.Classification,
            NotForNavigation = c.NotForNavigation,
            ProductSpecificationName = c.ProductSpecificationName,
            ProductSpecificationVersion = c.ProductSpecificationVersion,
            GeometryKind = c.GeometryKind,
            Coordinates = c.Coordinates,
            OnlineResources = c.OnlineResources,
            ExtraAttributes = c.ExtraAttributes,
        };
    }

    private static S128PhysicalProduct ProjectPhysicalProduct(S128Feature f, ProjectionContext ctx)
    {
        var c = ExtractCommon(f, ctx);
        return new S128PhysicalProduct
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            Source = f,
            ProductNumber = c.ProductNumber,
            EditionNumber = c.EditionNumber,
            UpdateNumber = c.UpdateNumber,
            IssueDate = c.IssueDate,
            UpdateDate = c.UpdateDate,
            Classification = c.Classification,
            NotForNavigation = c.NotForNavigation,
            ProductSpecificationName = c.ProductSpecificationName,
            ProductSpecificationVersion = c.ProductSpecificationVersion,
            GeometryKind = c.GeometryKind,
            Coordinates = c.Coordinates,
            OnlineResources = c.OnlineResources,
            ExtraAttributes = c.ExtraAttributes,
        };
    }

    private static S128Service ProjectService(S128Feature f, ProjectionContext ctx)
    {
        var c = ExtractCommon(f, ctx);
        return new S128Service
        {
            Id = f.Id,
            FeatureType = f.FeatureType,
            Source = f,
            ServiceStatus = ClassifyServiceStatus(f.Attributes.GetValueOrDefault("serviceStatus")),
            ProductNumber = c.ProductNumber,
            EditionNumber = c.EditionNumber,
            UpdateNumber = c.UpdateNumber,
            IssueDate = c.IssueDate,
            UpdateDate = c.UpdateDate,
            Classification = c.Classification,
            NotForNavigation = c.NotForNavigation,
            ProductSpecificationName = c.ProductSpecificationName,
            ProductSpecificationVersion = c.ProductSpecificationVersion,
            GeometryKind = c.GeometryKind,
            Coordinates = c.Coordinates,
            OnlineResources = c.OnlineResources,
            ExtraAttributes = c.ExtraAttributes,
        };
    }

    private static S128ServiceStatus ClassifyServiceStatus(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return S128ServiceStatus.Unknown;
        if (raw == "1" || raw.Contains("Planned", StringComparison.OrdinalIgnoreCase))
            return S128ServiceStatus.Planned;
        if (raw == "2" || raw.Contains("Released", StringComparison.OrdinalIgnoreCase))
            return S128ServiceStatus.Released;
        if (raw == "3" || raw.Contains("Withdrawn", StringComparison.OrdinalIgnoreCase))
            return S128ServiceStatus.Withdrawn;
        return S128ServiceStatus.Unknown;
    }

    private static ImmutableDictionary<string, string> BuildExtraAttributes(S128Feature f) =>
        ExtraAttributes.ExcludeKnown(f.Attributes,
            "productNumber", "datasetName",
            "editionNumber", "updateNumber",
            "issueDate", "updateDate", "editionDate",
            "catalogueElementClassification",
            "notForNavigation",
            "serviceStatus");

    private static S128ProducerInformation ProjectProducer(S128Feature f) => new()
    {
        Id = f.Id,
        AgencyResponsibleForProduction = f.Attributes.GetValueOrDefault("agencyResponsibleForProduction"),
        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "agencyResponsibleForProduction"),
        Source = f,
    };

    private static S128DistributorInformation ProjectDistributor(S128Feature f) => new()
    {
        Id = f.Id,
        DistributorName = f.Attributes.GetValueOrDefault("distributorName"),
        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "distributorName"),
        Source = f,
    };

    private static S128ContactDetails ProjectContact(S128Feature f) => new()
    {
        Id = f.Id,
        ContactInstructions = f.Attributes.GetValueOrDefault("contactInstructions"),
        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "contactInstructions"),
        Source = f,
    };

    private static S128CatalogueSectionHeader ProjectSectionHeader(S128Feature f) => new()
    {
        Id = f.Id,
        CatalogueSectionNumber = f.Attributes.GetValueOrDefault("catalogueSectionNumber"),
        ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes, "catalogueSectionNumber"),
        Source = f,
    };

    private static (S128GeometryKind, ImmutableArray<GeoPosition>) ProjectGeometry(S128Feature f)
    {
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                if (f.Points.IsDefaultOrEmpty) return (S128GeometryKind.Point, ImmutableArray<GeoPosition>.Empty);
                return (S128GeometryKind.Point, f.Points
                    .Select(p => new GeoPosition(p.Latitude, p.Longitude))
                    .ToImmutableArray());
            case GmlGeometryType.Curve:
                if (f.Curves.IsDefaultOrEmpty) return (S128GeometryKind.Curve, ImmutableArray<GeoPosition>.Empty);
                return (S128GeometryKind.Curve, f.Curves
                    .SelectMany(c => c)
                    .Select(p => new GeoPosition(p.Latitude, p.Longitude))
                    .ToImmutableArray());
            case GmlGeometryType.Surface:
                if (f.ExteriorRing.IsDefaultOrEmpty) return (S128GeometryKind.Surface, ImmutableArray<GeoPosition>.Empty);
                return (S128GeometryKind.Surface, f.ExteriorRing
                    .Select(p => new GeoPosition(p.Latitude, p.Longitude))
                    .ToImmutableArray());
            default:
                return (S128GeometryKind.None, ImmutableArray<GeoPosition>.Empty);
        }
    }

    private static ImmutableArray<S128OnlineResource> ProjectOnlineResources(S128Feature f)
    {
        var b = ImmutableArray.CreateBuilder<S128OnlineResource>();
        foreach (var c in f.ComplexAttributes)
        {
            if (!c.Code.Equals("onlineResource", StringComparison.OrdinalIgnoreCase)) continue;
            b.Add(new S128OnlineResource
            {
                ApplicationProfile = c.SubAttributes.GetValueOrDefault("applicationProfile"),
                Linkage = c.SubAttributes.GetValueOrDefault("linkage"),
            });
        }
        return b.ToImmutable();
    }

    // ── theReference resolution ────────────────────────────────────────

    private static void ResolveProductReferences(
        S128CatalogueEntry entry,
        ProjectionContext ctx,
        Dictionary<string, List<S128CatalogueEntry>> supersedes,
        Dictionary<string, List<S128ProductReference>> related)
    {
        var feature = entry.Source;

        // Collect theReference xlinks and their companion ProductMapping
        // complex-attribute payloads. The reader walks element children in
        // source order in both ParseReferences and ParseAttributes, so the
        // i-th theReference xlink lines up with the i-th theReference
        // complex attribute.
        var xlinks = new List<S128XlinkReference>();
        foreach (var r in feature.References)
        {
            if (r.Role.Equals("theReference", StringComparison.OrdinalIgnoreCase))
                xlinks.Add(r);
        }
        if (xlinks.Count == 0) return;

        var payloads = new List<S128ComplexAttribute>();
        foreach (var c in feature.ComplexAttributes)
        {
            if (c.Code.Equals("theReference", StringComparison.OrdinalIgnoreCase))
                payloads.Add(c);
        }

        var count = Math.Min(xlinks.Count, payloads.Count == 0 ? xlinks.Count : payloads.Count);
        for (var i = 0; i < count; i++)
        {
            var x = xlinks[i];
            var payload = i < payloads.Count ? payloads[i] : null;

            string? rawCategory = null;
            if (payload is not null)
            {
                foreach (var nested in payload.NestedAttributes)
                {
                    if (!nested.Code.Equals("ProductMapping", StringComparison.OrdinalIgnoreCase)) continue;
                    rawCategory = nested.SubAttributes.GetValueOrDefault("categoryOfProductMapping");
                    if (rawCategory is not null) break;
                }
            }

            var category = ClassifyMappingCategory(rawCategory);

            var target = ctx.Xlinks.Resolve<S128CatalogueEntry>(x.Href, "theReference", ctx, feature.Id);
            if (target is null) continue;

            if (category == S128ProductMappingCategory.HigherPriorityAlternative)
            {
                if (!supersedes.TryGetValue(entry.Id, out var list))
                    supersedes[entry.Id] = list = new List<S128CatalogueEntry>();
                list.Add(target);
            }
            else
            {
                if (!related.TryGetValue(entry.Id, out var list))
                    related[entry.Id] = list = new List<S128ProductReference>();
                list.Add(new S128ProductReference(target, category, rawCategory));
            }
        }
    }

    /// <summary>
    /// Maps a raw <c>categoryOfProductMapping</c> value to a typed enum
    /// (S-128 § 12). Recognises both the numeric code <c>1</c> and the
    /// localised label "Higher Priority Alternative" published by the
    /// upstream 2.0.0 sample.
    /// </summary>
    private static S128ProductMappingCategory ClassifyMappingCategory(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return S128ProductMappingCategory.Unknown;
        if (raw == "1" || raw.Contains("Higher Priority Alternative", StringComparison.OrdinalIgnoreCase))
            return S128ProductMappingCategory.HigherPriorityAlternative;
        return S128ProductMappingCategory.Other;
    }
}
