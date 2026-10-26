using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="ListSpecsTool"/>.</summary>
public sealed record ListSpecsRequest();

/// <summary>
/// Per-spec summary returned by <see cref="ListSpecsTool"/>.
/// </summary>
/// <param name="Name">Canonical spec name (<c>"S-NNN"</c>, or a non-S-100 format name such as <c>"GRIB2"</c>).</param>
/// <param name="LoadedDatasetCount">Number of datasets of this spec currently loaded.</param>
/// <param name="Capabilities">
/// Capability flags advertising which tools the agent can productively
/// invoke against this spec in the current session.
/// </param>
/// <param name="PortrayalNote">
/// For a product whose specification defines no portrayal (S-104), says the
/// portrayal is SoundCharts' own (<see cref="PortrayalBasis.ApplicationBasis.Note"/>);
/// <c>null</c> for products drawn from an official portrayal catalogue.
/// </param>
/// <param name="IsS100">
/// Whether the product is an IHO S-100 product
/// (<see cref="ProductRegistration.IsS100"/>); <c>false</c> for a format such
/// as a weather model grid, which isn't for navigation.
/// </param>
public sealed record SpecSummary(
    [property: Description("Canonical spec name, e.g. \"S-101\", \"S-124\", or a non-S-100 format name such as \"GRIB2\".")] string Name,
    [property: Description("Number of datasets of this spec currently loaded in the host catalog.")] int LoadedDatasetCount,
    [property: Description("Capability flags advertising which MCP tools the agent can productively invoke against this spec in the current session.")] SpecCapabilities Capabilities,
    [property: Description("Set when the specification defines no portrayal, so what render_to_image draws is SoundCharts' own symbology, not IHO's (S-104: \"SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology\"). Null for products drawn from an official portrayal catalogue.")] string? PortrayalNote = null,
    [property: Description("True for an IHO S-100 product (and legacy S-57). False for a product that isn't S-100, such as weather model data, which is not for navigation.")] bool IsS100 = true);

/// <summary>
/// Per-spec capability flags. All flags are conservative — they reflect
/// what the codebase actually implements today, not what the spec
/// *could* in principle support.
/// </summary>
/// <param name="CanQueryFeatures">
/// <c>true</c> when <see cref="QueryFeaturesTool"/> can enumerate
/// features for this spec (i.e. the spec is a GML-encoded vector
/// product whose <c>LoadedDatasetData</c> variant is wired into
/// <c>FeatureAccessor</c>).
/// </param>
/// <param name="CanDescribeFeature">
/// <c>true</c> when <see cref="DescribeFeatureTool"/> has a describer
/// registered in <see cref="Spec.FeatureDescriberRegistry.Default"/>
/// for this spec.
/// </param>
/// <param name="CanSampleCoverage">
/// <c>true</c> when <see cref="SampleCoverageTool"/> can sample a
/// scalar / vector value at a point for this spec.
/// </param>
/// <param name="CanListTimeSteps">
/// <c>true</c> when <see cref="ListTimeStepsTool"/> can enumerate time
/// steps for this spec (i.e. it is a time-varying coverage product).
/// </param>
public sealed record SpecCapabilities(
    [property: Description("True when query_features can enumerate features for this spec.")] bool CanQueryFeatures,
    [property: Description("True when describe_feature has a describer registered for this spec.")] bool CanDescribeFeature,
    [property: Description("True when sample_coverage can sample a scalar/vector value at a point for this spec.")] bool CanSampleCoverage,
    [property: Description("True when list_time_steps can enumerate time steps for this spec (time-varying coverage products only).")] bool CanListTimeSteps);

/// <summary>Result of <see cref="ListSpecsTool"/>.</summary>
public sealed record ListSpecsResult([property: Description("Per-spec summaries in catalog order, one entry per known spec.")] IReadOnlyList<SpecSummary> Specs);

/// <summary>
/// Returns the product specifications the host can load, the number of
/// datasets of each spec currently loaded, and per-spec capability flags.
/// Helps the agent introspect what tools are productive against the current
/// session before issuing them.
/// </summary>
/// <remarks>
/// The known specs are the products in the <see cref="ProductRegistry"/>
/// (by default <see cref="ProductRegistry.Default"/>), and each summary's
/// <see cref="SpecSummary.IsS100"/> and <see cref="SpecSummary.PortrayalNote"/>
/// come from the product's registration, so a host that registers a product
/// sees it listed without changing this tool. Datasets of unregistered specs
/// that somehow appear in the catalog are still surfaced — the summary list is
/// the union of the registered specs and the spec names observed in the
/// catalog snapshot.
/// </remarks>
public sealed class ListSpecsTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "list_specs";

    // Specs whose features are exposed via the shared IS100Feature
    // interface and addressable by QueryFeaturesTool. Must match the
    // switch arms in Spec.FeatureAccessor.GetFeatures. S-101 is
    // ISO 8211-encoded but its pipeline Feature records implement
    // IS100Feature directly.
    private static readonly HashSet<string> GmlVectorSpecs = new(StringComparer.Ordinal)
    {
        "S-101",
        "S-122", "S-124", "S-125", "S-127", "S-128",
        "S-129", "S-131", "S-201", "S-401", "S-411", "S-421",
    };

    // Specs SampleCoverageTool routes to a sampler.
    private static readonly HashSet<string> CoverageSpecs = new(StringComparer.Ordinal)
    {
        "S-102", "S-104", "S-111",
    };

    // Time-varying coverage specs (subset of CoverageSpecs) that
    // ListTimeStepsTool can enumerate. S-102 is static.
    private static readonly HashSet<string> TimeVaryingCoverageSpecs = new(StringComparer.Ordinal)
    {
        "S-104", "S-111",
    };

    // Specs with a registered describer.
    private static readonly HashSet<string> DescribableSpecs = new(StringComparer.Ordinal)
    {
        "S-101", "S-102", "S-104", "S-111", "S-124",
        "S-122", "S-125", "S-127", "S-128", "S-129",
        "S-131", "S-201", "S-401", "S-411", "S-421",
    };

    private readonly IDatasetCatalog _catalog;
    private readonly ProductRegistry _registry;

    /// <summary>Creates a new <see cref="ListSpecsTool"/>.</summary>
    /// <param name="catalog">The host's dataset catalog.</param>
    /// <param name="registry">
    /// The products the host can load; <see langword="null"/> for
    /// <see cref="ProductRegistry.Default"/>.
    /// </param>
    public ListSpecsTool(IDatasetCatalog catalog, ProductRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _registry = registry ?? ProductRegistry.Default;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<ListSpecsResult>> InvokeAsync(
        ListSpecsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _catalog.Datasets;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var dataset in snapshot)
        {
            counts.TryGetValue(dataset.Spec.Name, out var count);
            counts[dataset.Spec.Name] = count + 1;
        }

        var names = new SortedSet<string>(_registry.RegisteredSpecs, StringComparer.Ordinal);
        foreach (var k in counts.Keys) names.Add(k);

        var summaries = new List<SpecSummary>(names.Count);
        foreach (var name in names)
        {
            counts.TryGetValue(name, out var loaded);
            var caps = new SpecCapabilities(
                CanQueryFeatures: GmlVectorSpecs.Contains(name),
                CanDescribeFeature: DescribableSpecs.Contains(name),
                CanSampleCoverage: CoverageSpecs.Contains(name),
                CanListTimeSteps: TimeVaryingCoverageSpecs.Contains(name));
            summaries.Add(new SpecSummary(
                name,
                loaded,
                caps,
                _registry.PortrayalOf(name).AsApplication?.Note,
                _registry.IsS100(name)));
        }

        return Task.FromResult(ToolResult<ListSpecsResult>.Ok(
            new ListSpecsResult(summaries)));
    }
}
