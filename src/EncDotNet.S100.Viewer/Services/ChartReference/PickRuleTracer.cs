using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Traces a picked feature's portrayal rule for Why this symbol (symbol
/// dictionary R12b, #1719): its dataset's processor portrays it again in a
/// LuaCSharp session with a line hook, with the display settings the map is
/// drawn with, off the UI thread.
/// </summary>
/// <remarks>
/// Only processors whose rules are Lua (<see cref="IRuleTraceSource"/>: an
/// S-101 dataset, or an S-57 cell drawn with S-101's catalogue) are traced.
/// Rules in XSLT, and any feature that isn't, keep the match. The processor
/// is leased while the trace runs, so a dataset closed meanwhile isn't
/// disposed under it.
/// </remarks>
internal sealed class PickRuleTracer
{
    private readonly IDatasetLoaderService _loader;
    private readonly Func<MarinerSettings> _mariner;
    private readonly Lazy<ILuaEngine> _engine;

    /// <summary>Creates the tracer.</summary>
    /// <param name="loader">The loader whose processors draw the map.</param>
    /// <param name="mariner">The display settings the map is drawn with, read when a trace starts.</param>
    /// <param name="engine">The engine to trace on; LuaCSharp when <see langword="null"/>, whichever engine draws the map.</param>
    public PickRuleTracer(IDatasetLoaderService loader, Func<MarinerSettings> mariner, ILuaEngine? engine = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(mariner);
        _loader = loader;
        _mariner = mariner;
        _engine = engine is null ? new(() => ViewerLuaEngine.Create(LuaEngineKind.LuaCSharp)) : new(engine);
    }

    /// <summary>
    /// Traces <paramref name="hit"/>'s rule off the calling thread.
    /// </summary>
    /// <param name="hit">The picked feature.</param>
    /// <param name="cancellationToken">Cancelled when the pick changes.</param>
    /// <returns>The trace, or <see langword="null"/> when the feature's rules aren't Lua or it isn't loaded.</returns>
    public Task<LuaFeatureTrace?> TraceAsync(PickHit hit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (hit.DatasetId is not { } datasetId || string.IsNullOrEmpty(hit.FeatureRef))
            return Task.FromResult<LuaFeatureTrace?>(null);
        // Read on the caller's thread, as the reasons read them.
        var mariner = _mariner();
        var feature = hit.FeatureRef;
        return Task.Run(
            async () =>
            {
                using var processors = _loader.AcquireProcessors();
                var source = processors
                    .Where(p => string.Equals(p.Key.Id.Value, datasetId, StringComparison.Ordinal))
                    .Select(p => p.Value)
                    .OfType<IRuleTraceSource>()
                    .FirstOrDefault();
                return source is null
                    ? null
                    : await source.TraceFeatureAsync(feature, _engine.Value, mariner, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }
}
