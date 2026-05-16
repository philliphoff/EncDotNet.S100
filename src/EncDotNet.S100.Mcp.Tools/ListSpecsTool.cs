using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="ListSpecsTool"/>.</summary>
public sealed record ListSpecsRequest();

/// <summary>
/// Per-spec summary returned by <see cref="ListSpecsTool"/>.
/// </summary>
/// <param name="Name">Canonical spec name (<c>"S-NNN"</c>).</param>
/// <param name="LoadedDatasetCount">Number of datasets of this spec currently loaded.</param>
/// <param name="Capabilities">
/// Capability flags advertising which tools the agent can productively
/// invoke against this spec in the current session.
/// </param>
public sealed record SpecSummary(
    string Name,
    int LoadedDatasetCount,
    SpecCapabilities Capabilities);

/// <summary>
/// Per-spec capability flags. All flags are conservative — they reflect
/// what the codebase actually implements today, not what the spec
/// *could* in principle support.
/// </summary>
/// <param name="CanQueryFeatures">
/// <c>true</c> when <see cref="QueryFeaturesTool"/> can enumerate
/// features for this spec (i.e. the spec is a GML-encoded vector
/// product whose <c>LoadedDatasetData</c> variant is wired into
/// <c>GmlFeatureAccessor</c>).
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
public sealed record SpecCapabilities(
    bool CanQueryFeatures,
    bool CanDescribeFeature,
    bool CanSampleCoverage);

/// <summary>Result of <see cref="ListSpecsTool"/>.</summary>
public sealed record ListSpecsResult(ImmutableArray<SpecSummary> Specs);

/// <summary>
/// Returns the product specifications this MCP-tools assembly knows
/// about, the number of datasets of each spec currently loaded, and
/// per-spec capability flags. Helps the agent introspect what tools
/// are productive against the current session before issuing them.
/// </summary>
/// <remarks>
/// The catalogue of known specs is fixed by the assembly (matches the
/// product set under <c>src/EncDotNet.S100.Datasets.*/</c>). Datasets
/// of unknown specs that somehow appear in the catalog are still
/// surfaced — the summary list is the union of the known-spec list
/// and the spec names observed in the catalog snapshot.
/// </remarks>
public sealed class ListSpecsTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "list_specs";

    // Canonical list of spec names this assembly ships support for.
    // Update in lockstep with EncDotNet.S100.Datasets.* projects.
    private static readonly string[] KnownSpecs =
    [
        "S-101",
        "S-102",
        "S-104",
        "S-111",
        "S-122",
        "S-124",
        "S-125",
        "S-127",
        "S-128",
        "S-129",
        "S-131",
        "S-201",
        "S-411",
        "S-421",
    ];

    // Specs whose features are exposed via the shared IGmlFeature
    // interface and addressable by QueryFeaturesTool. Must match the
    // switch arms in Spec.GmlFeatureAccessor.GetFeatures.
    private static readonly HashSet<string> GmlVectorSpecs = new(StringComparer.Ordinal)
    {
        "S-122", "S-124", "S-125", "S-127", "S-128",
        "S-129", "S-131", "S-201", "S-411", "S-421",
    };

    // Specs SampleCoverageTool routes to a sampler.
    private static readonly HashSet<string> CoverageSpecs = new(StringComparer.Ordinal)
    {
        "S-102", "S-104", "S-111",
    };

    // Specs with a registered describer.
    private static readonly HashSet<string> DescribableSpecs = new(StringComparer.Ordinal)
    {
        "S-101", "S-102", "S-104", "S-111", "S-124",
        "S-122", "S-125", "S-127", "S-128", "S-129",
        "S-131", "S-201", "S-411", "S-421",
    };

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="ListSpecsTool"/>.</summary>
    public ListSpecsTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
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

        var names = new SortedSet<string>(KnownSpecs, StringComparer.Ordinal);
        foreach (var k in counts.Keys) names.Add(k);

        var summaries = ImmutableArray.CreateBuilder<SpecSummary>(names.Count);
        foreach (var name in names)
        {
            counts.TryGetValue(name, out var loaded);
            var caps = new SpecCapabilities(
                CanQueryFeatures: GmlVectorSpecs.Contains(name),
                CanDescribeFeature: DescribableSpecs.Contains(name),
                CanSampleCoverage: CoverageSpecs.Contains(name));
            summaries.Add(new SpecSummary(name, loaded, caps));
        }

        return Task.FromResult(ToolResult<ListSpecsResult>.Ok(
            new ListSpecsResult(summaries.MoveToImmutable())));
    }
}
