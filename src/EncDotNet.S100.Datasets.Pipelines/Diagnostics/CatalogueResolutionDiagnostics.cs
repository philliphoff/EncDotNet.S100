using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines.Diagnostics;

/// <summary>
/// Surfaces a structured warning when the version of a portrayal or feature
/// catalogue resolved for a dataset diverges from the dataset's declared
/// product specification edition.
/// </summary>
/// <remarks>
/// <para>
/// Per S-100 Edition 5.2.1 Part 2 §6, a catalogue at a higher minor version
/// than the dataset is a backward-compatible read; a lower minor or a
/// different major may indicate the wrong catalogue is wired up. We emit one
/// diagnostic per processor instance per (dataset spec, catalogue ref) pair —
/// repeated calls with the same scope do not re-emit.
/// </para>
/// <para>
/// Each emit:
/// <list type="bullet">
///   <item><description>Adds an event named <c>s100.catalogue.match</c> to
///   the current <see cref="Activity"/> when one is in scope.</description></item>
///   <item><description>Increments the
///   <c>s100.catalogue.match.count</c> counter, tagged with the spec name,
///   declared edition, catalogue version, catalogue kind, and match kind.</description></item>
/// </list>
/// </para>
/// <para>
/// Dataset processors should call this from their constructor — catalogue
/// resolution is a one-shot per processor lifetime, and emitting at
/// construction keeps the per-<see cref="IDatasetProcessor.Render"/> hot
/// path free of any telemetry overhead.
/// </para>
/// </remarks>
public static class CatalogueResolutionDiagnostics
{
    private static readonly Counter<long> _matchCounter = Telemetry.Meter.CreateCounter<long>(
        "s100.catalogue.match.count",
        unit: "{events}",
        description: "Counts catalogue resolutions, tagged by spec/catalogue versions and match kind.");

    /// <summary>
    /// Reports the relationship between a dataset's declared spec and the
    /// catalogue resolved for it. Intended to be called once per processor
    /// instance from the processor's constructor, after the catalogue has
    /// been assigned.
    /// </summary>
    /// <param name="dedupScope">
    /// An object whose identity scopes deduplication — typically the
    /// processor instance itself. Pass <c>this</c>. Subsequent calls with
    /// the same <paramref name="dedupScope"/>, <paramref name="datasetSpec"/>,
    /// <paramref name="catalogueRef"/>, and <paramref name="catalogueKind"/>
    /// are no-ops; this safety net guards against accidental double-emit
    /// without forcing the caller to manually track state.
    /// </param>
    /// <param name="datasetSpec">The dataset's declared product spec.</param>
    /// <param name="catalogueRef">
    /// The catalogue's identity, or <c>null</c> when the catalogue does not
    /// self-describe (older bundled assets).
    /// </param>
    /// <param name="catalogueKind">
    /// A short description of which catalogue this is — typically
    /// <c>"portrayal"</c> or <c>"feature"</c>.
    /// </param>
    public static void Report(
        object dedupScope,
        SpecRef datasetSpec,
        CatalogueRef? catalogueRef,
        string catalogueKind)
    {
        ArgumentNullException.ThrowIfNull(dedupScope);
        ArgumentException.ThrowIfNullOrEmpty(catalogueKind);

        if (catalogueRef is not { } catRef) return;

        var key = (datasetSpec, catRef, catalogueKind);
        var entries = _seen.GetOrCreateValue(dedupScope);
        if (!entries.Add(key))
        {
            // Already emitted for this scope/spec/cat tuple.
            return;
        }

        var match = SpecCompatibility.Classify(datasetSpec.Edition, catRef.Version);

        _matchCounter.Add(1,
            new KeyValuePair<string, object?>("s100.spec.name", datasetSpec.Name),
            new KeyValuePair<string, object?>("s100.spec.edition", datasetSpec.Edition.ToString()),
            new KeyValuePair<string, object?>("s100.catalogue.version", catRef.Version.ToString()),
            new KeyValuePair<string, object?>("s100.catalogue.kind", catalogueKind),
            new KeyValuePair<string, object?>("s100.catalogue.match", match.ToString()));

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.AddEvent(new ActivityEvent("s100.catalogue.match", tags: new ActivityTagsCollection
            {
                ["s100.spec.name"] = datasetSpec.Name,
                ["s100.spec.edition"] = datasetSpec.Edition.ToString(),
                ["s100.catalogue.version"] = catRef.Version.ToString(),
                ["s100.catalogue.kind"] = catalogueKind,
                ["s100.catalogue.match"] = match.ToString(),
            }));
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, HashSet<(SpecRef, CatalogueRef, string)>> _seen = new();
}
