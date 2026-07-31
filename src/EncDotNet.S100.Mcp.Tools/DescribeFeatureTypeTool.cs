using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="DescribeFeatureTypeTool"/>.
/// </summary>
/// <param name="Spec">
/// The product specification whose bundled Feature Catalogue to inspect
/// (e.g. <c>S-101</c> or <c>S-124/1.5.0</c>). The edition is ignored —
/// one Feature Catalogue is bundled per spec.
/// </param>
/// <param name="FeatureType">
/// Optional feature-type code, name, or alias (case-insensitive). When
/// <c>null</c>, the tool lists every feature type in the catalogue with
/// its attribute count but no per-attribute detail. When supplied, the
/// matched feature type is returned with its full attribute bindings.
/// </param>
/// <param name="IncludeListedValues">
/// When <c>true</c> (default) enumerated attributes carry their full set
/// of listed values. Set <c>false</c> to omit them and keep the payload
/// small for feature types with large enumerations.
/// </param>
public sealed record DescribeFeatureTypeRequest(
    [property: Description("Product specification whose bundled Feature Catalogue to inspect (e.g. \"S-101\" or \"S-124/1.5.0\"). Edition is ignored.")] SpecRef Spec,
    [property: Description("Optional feature-type code, name, or alias (case-insensitive). Null lists every feature type with attribute counts only; supplied returns full attribute detail for the match.")] string? FeatureType = null,
    [property: Description("When true (default), enumerated attributes carry their full listed values; set false to omit them.")] bool IncludeListedValues = true);

/// <summary>A single permitted enumeration value of an attribute.</summary>
/// <param name="Code">The enumeration code (often a numeric string).</param>
/// <param name="Label">Human-readable label.</param>
/// <param name="Definition">Optional definition text.</param>
public sealed record ListedValueInfo(
    [property: Description("The enumeration code (often a numeric string).")] string Code,
    [property: Description("Human-readable label.")] string Label,
    [property: Description("Optional definition text.")] string? Definition);

/// <summary>An attribute bound to a feature type in the Feature Catalogue.</summary>
/// <param name="Code">The attribute code (the GML element local name / S-101 acronym).</param>
/// <param name="Name">Human-readable attribute name.</param>
/// <param name="ValueType">Catalogue value type (e.g. <c>CharacterString</c>, <c>Real</c>, <c>Enumeration</c>); <c>complexAttribute</c> for complex attributes.</param>
/// <param name="Mandatory"><c>true</c> when the binding's lower multiplicity is &gt; 0.</param>
/// <param name="Repeatable"><c>true</c> when the binding's upper multiplicity is unbounded or &gt; 1.</param>
/// <param name="IsComplex"><c>true</c> when the attribute is a complex (nested) attribute rather than a simple one.</param>
/// <param name="ListedValues">Permitted enumeration values (empty when not enumerated or when omitted by request).</param>
/// <param name="PermittedValues">When the binding constrains the attribute to a subset of its listed values, those codes; otherwise empty.</param>
public sealed record AttributeInfo(
    [property: Description("The attribute code (GML element local name / S-101 acronym).")] string Code,
    [property: Description("Human-readable attribute name.")] string Name,
    [property: Description("Catalogue value type (e.g. CharacterString, Real, Enumeration); 'complexAttribute' for complex attributes.")] string ValueType,
    [property: Description("True when the binding's lower multiplicity is > 0.")] bool Mandatory,
    [property: Description("True when the binding's upper multiplicity is unbounded or > 1.")] bool Repeatable,
    [property: Description("True when the attribute is a complex (nested) attribute rather than a simple one.")] bool IsComplex,
    [property: Description("Permitted enumeration values (empty when not enumerated or omitted by request).")] IReadOnlyList<ListedValueInfo> ListedValues,
    [property: Description("When the binding constrains the attribute to a subset of its listed values, those codes; otherwise empty.")] IReadOnlyList<string> PermittedValues);

/// <summary>A feature type defined in the Feature Catalogue.</summary>
/// <param name="Code">The feature-type code.</param>
/// <param name="Name">Human-readable feature-type name.</param>
/// <param name="Definition">Optional definition text.</param>
/// <param name="IsAbstract"><c>true</c> for abstract super-types that are not instantiated directly.</param>
/// <param name="SuperType">Code of the parent feature type, if any.</param>
/// <param name="PermittedPrimitives">Geometry primitives the feature type may carry (e.g. <c>point</c>, <c>curve</c>, <c>surface</c>).</param>
/// <param name="AttributeCount">Number of attribute bindings on the feature type.</param>
/// <param name="Attributes">Attribute detail (populated only when a specific feature type was requested).</param>
public sealed record FeatureTypeInfo(
    [property: Description("The feature-type code.")] string Code,
    [property: Description("Human-readable feature-type name.")] string Name,
    [property: Description("Optional definition text.")] string? Definition,
    [property: Description("True for abstract super-types that are not instantiated directly.")] bool IsAbstract,
    [property: Description("Code of the parent feature type, if any.")] string? SuperType,
    [property: Description("Geometry primitives the feature type may carry.")] IReadOnlyList<string> PermittedPrimitives,
    [property: Description("Number of attribute bindings on the feature type.")] int AttributeCount,
    [property: Description("Attribute detail (populated only when a specific feature type was requested).")] IReadOnlyList<AttributeInfo> Attributes);

/// <summary>Result of <see cref="DescribeFeatureTypeTool"/>.</summary>
/// <param name="Spec">Echoed spec.</param>
/// <param name="CatalogueName">Name of the bundled Feature Catalogue.</param>
/// <param name="CatalogueVersion">Version number of the bundled Feature Catalogue.</param>
/// <param name="FeatureTypes">The matched feature type (detail mode) or every feature type (list mode).</param>
/// <param name="TotalFeatureTypeCount">Total feature types in the catalogue.</param>
public sealed record DescribeFeatureTypeResult(
    [property: Description("Echoed spec.")] SpecRef Spec,
    [property: Description("Name of the bundled Feature Catalogue.")] string CatalogueName,
    [property: Description("Version number of the bundled Feature Catalogue.")] string CatalogueVersion,
    [property: Description("The matched feature type (detail mode) or every feature type (list mode).")] IReadOnlyList<FeatureTypeInfo> FeatureTypes,
    [property: Description("Total feature types in the catalogue.")] int TotalFeatureTypeCount);

/// <summary>
/// Introspects a spec's bundled Feature Catalogue (ISO 19110 / S-100
/// Part 5): lists its feature types and, for a chosen type, the
/// attributes it may carry — their value types, mandatory / repeatable
/// multiplicity, and enumerated values.
/// </summary>
/// <remarks>
/// <para>
/// This is the schema-discovery counterpart to the data-discovery tools
/// (<see cref="CountFeaturesTool"/>, <see cref="QueryFeaturesTool"/>): it
/// answers "what attributes is a <c>BuoyLateral</c> allowed to have, and
/// what are the legal values of its <c>categoryOfLateralMark</c>?" so an
/// agent can build valid attribute predicates without a loaded dataset.
/// </para>
/// <para>
/// Operates purely over the bundled catalogues exposed by
/// <see cref="Specification"/>; it does not consult the dataset catalog.
/// The spec name is normalised (casing and an optional edition suffix
/// such as <c>S-101/1.2.0</c> are accepted), so a slightly-off but
/// recognisable name resolves successfully. Specs without a bundled
/// Feature Catalogue return <see cref="FeatureCatalogueNotAvailable"/>,
/// whose <c>AcceptedSpecs</c> list the spec names that do have one.
/// </para>
/// </remarks>
public sealed class DescribeFeatureTypeTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "describe_feature_type";

    private readonly FeatureCatalogueManager _catalogues;
    private readonly IReadOnlyList<string> _acceptedSpecs;

    /// <summary>
    /// Creates a tool backed by the bundled Feature Catalogues exposed
    /// through <see cref="Specification.TryOpenFeatureCatalogue"/>.
    /// </summary>
    public DescribeFeatureTypeTool()
        : this(
            Specification.TryOpenFeatureCatalogue,
            Specification.AvailableSpecs.Where(Specification.HasFeatureCatalogue))
    {
    }

    /// <summary>
    /// Creates a tool backed by a custom Feature Catalogue resolver
    /// (primarily for testing with synthetic catalogues).
    /// </summary>
    /// <param name="catalogueResolver">Maps a spec name (e.g. <c>S-124</c>) to a Feature Catalogue XML stream, or <c>null</c> when unavailable.</param>
    /// <param name="acceptedSpecs">
    /// The canonical spec names the resolver can serve. Surfaced in the
    /// <see cref="FeatureCatalogueNotAvailable"/> error so a caller who
    /// passed a slightly-off name can self-correct. Defaults to empty.
    /// </param>
    public DescribeFeatureTypeTool(Func<string, Stream?> catalogueResolver, IEnumerable<string>? acceptedSpecs = null)
    {
        ArgumentNullException.ThrowIfNull(catalogueResolver);
        _catalogues = new FeatureCatalogueManager(catalogueResolver);
        _acceptedSpecs = acceptedSpecs is null
            ? []
            : acceptedSpecs.ToArray();
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<DescribeFeatureTypeResult>> InvokeAsync(
        DescribeFeatureTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Spec.Name))
        {
            return Task.FromResult(ToolResult<DescribeFeatureTypeResult>.Err(
                new InvalidArgument("spec", "a product specification name is required")));
        }

        FeatureCatalogue? catalogue;
        try
        {
            catalogue = _catalogues.GetCatalogue(request.Spec.Name);
        }
        catch (Exception)
        {
            catalogue = null;
        }

        if (catalogue is null)
        {
            return Task.FromResult(ToolResult<DescribeFeatureTypeResult>.Err(
                new FeatureCatalogueNotAvailable(request.Spec, _acceptedSpecs)));
        }

        var simpleByCode = new Dictionary<string, SimpleAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in catalogue.SimpleAttributes)
        {
            simpleByCode[attr.Code] = attr;
        }

        var complexByCode = new Dictionary<string, ComplexAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in catalogue.ComplexAttributes)
        {
            complexByCode[attr.Code] = attr;
        }

        var total = catalogue.FeatureTypes.Count;

        if (request.FeatureType is { } requested && !string.IsNullOrWhiteSpace(requested))
        {
            var match = catalogue.FeatureTypes.FirstOrDefault(ft =>
                string.Equals(ft.Code, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ft.Name, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ft.Alias, requested, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return Task.FromResult(ToolResult<DescribeFeatureTypeResult>.Err(
                    new FeatureTypeNotFound(request.Spec, requested)));
            }

            var info = Describe(match, simpleByCode, complexByCode, request.IncludeListedValues);
            return Ok(request.Spec, catalogue, [info], total);
        }

        var builder = new List<FeatureTypeInfo>(total);
        foreach (var ft in catalogue.FeatureTypes.OrderBy(ft => ft.Code, StringComparer.Ordinal))
        {
            builder.Add(new FeatureTypeInfo(
                ft.Code,
                ft.Name,
                ft.Definition,
                ft.IsAbstract,
                ft.SuperType,
                ft.PermittedPrimitives.ToArray(),
                ft.AttributeBindings.Count,
                []));
        }

        return Ok(request.Spec, catalogue, builder, total);
    }

    private static Task<ToolResult<DescribeFeatureTypeResult>> Ok(
        SpecRef spec,
        FeatureCatalogue catalogue,
        IReadOnlyList<FeatureTypeInfo> featureTypes,
        int total) =>
        Task.FromResult(ToolResult<DescribeFeatureTypeResult>.Ok(
            new DescribeFeatureTypeResult(
                spec,
                catalogue.Name,
                catalogue.VersionNumber,
                featureTypes,
                total)));

    private static FeatureTypeInfo Describe(
        FeatureType featureType,
        IReadOnlyDictionary<string, SimpleAttribute> simpleByCode,
        IReadOnlyDictionary<string, ComplexAttribute> complexByCode,
        bool includeListedValues)
    {
        var attributes = new List<AttributeInfo>(featureType.AttributeBindings.Count);
        foreach (var binding in featureType.AttributeBindings)
        {
            var mandatory = binding.Multiplicity.Lower > 0;
            var repeatable = binding.Multiplicity.IsInfinite
                || binding.Multiplicity.Upper is null
                || binding.Multiplicity.Upper > 1;

            if (simpleByCode.TryGetValue(binding.AttributeRef, out var simple))
            {
                var listed = includeListedValues
                    ? simple.ListedValues
                        .Select(v => new ListedValueInfo(v.Code, v.Label, v.Definition))
                        .ToArray()
                    : [];

                attributes.Add(new AttributeInfo(
                    simple.Code,
                    simple.Name,
                    simple.ValueType,
                    mandatory,
                    repeatable,
                    IsComplex: false,
                    listed,
                    binding.PermittedValues.ToArray()));
            }
            else if (complexByCode.TryGetValue(binding.AttributeRef, out var complex))
            {
                attributes.Add(new AttributeInfo(
                    complex.Code,
                    complex.Name,
                    "complexAttribute",
                    mandatory,
                    repeatable,
                    IsComplex: true,
                    [],
                    binding.PermittedValues.ToArray()));
            }
            else
            {
                attributes.Add(new AttributeInfo(
                    binding.AttributeRef,
                    binding.AttributeRef,
                    "unknown",
                    mandatory,
                    repeatable,
                    IsComplex: false,
                    [],
                    binding.PermittedValues.ToArray()));
            }
        }

        return new FeatureTypeInfo(
            featureType.Code,
            featureType.Name,
            featureType.Definition,
            featureType.IsAbstract,
            featureType.SuperType,
            featureType.PermittedPrimitives.ToArray(),
            featureType.AttributeBindings.Count,
            attributes);
    }
}
