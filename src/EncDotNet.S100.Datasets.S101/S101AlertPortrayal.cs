using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// An alert a feature's portrayal raises (S-100 Part 9 <c>alertReference</c>),
/// and where: a sounding raises its own for each of its points.
/// </summary>
/// <param name="Alert">The alert.</param>
/// <param name="Position">
/// The point it's raised at, for an alert on an augmented point (an S-101
/// sounding's depth), or <see langword="null"/> when it's raised for the
/// feature's own geometry.
/// </param>
public sealed record FeatureAlert(PortrayalAlertReference Alert, GeoPosition? Position = null);

/// <summary>
/// Runs an S-101 cell's portrayal rules for chosen features and reports the
/// alerts each raises (<c>AlertReference</c>), at the context parameters a
/// route check gives: the S-101 <c>NavHazard</c>, <c>ProhAre</c> and
/// <c>SafetyContour</c> alerts depend on the safety contour.
/// </summary>
/// <remarks>
/// One Lua state is kept per set of mariner settings and each feature is
/// portrayed once, so a route check that asks leg by leg pays the rules'
/// set-up once. Thread-safe.
/// </remarks>
public sealed class S101AlertPortrayal : IDisposable
{
    private readonly Func<CancellationToken, Task<LuaRuleExecutor>> _createExecutor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<FeatureAlert>> _alerts = new(StringComparer.Ordinal);
    private LuaRuleExecutor? _executor;
    private MarinerSettings? _mariner;
    private LuaPortrayalSession? _session;

    /// <summary>Creates the alert portrayal for a cell.</summary>
    /// <param name="luaEngine">The Lua engine.</param>
    /// <param name="dataset">The cell.</param>
    /// <param name="catalogue">The S-101 (or S-401) portrayal catalogue.</param>
    /// <param name="featureCatalogue">The matching feature catalogue.</param>
    public S101AlertPortrayal(
        ILuaEngine luaEngine,
        S101Dataset dataset,
        S101PortrayalCatalogue catalogue,
        FeatureCatalogue featureCatalogue)
        : this(luaEngine, dataset, _ => Task.FromResult((catalogue, featureCatalogue)))
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(featureCatalogue);
    }

    /// <summary>
    /// Creates the alert portrayal for a cell whose catalogues are loaded on
    /// first use, for hosts that read cells without portraying them.
    /// </summary>
    /// <param name="luaEngine">The Lua engine.</param>
    /// <param name="dataset">The cell.</param>
    /// <param name="catalogues">Loads the portrayal and feature catalogues.</param>
    public S101AlertPortrayal(
        ILuaEngine luaEngine,
        S101Dataset dataset,
        Func<CancellationToken, Task<(S101PortrayalCatalogue Portrayal, FeatureCatalogue Features)>> catalogues)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(catalogues);

        // No anchors, transforms or spatial references: alerts need none of
        // the drawn geometry, and the anchors cost a pass over every feature.
        _createExecutor = async cancellationToken =>
        {
            var (portrayal, features) = await catalogues(cancellationToken).ConfigureAwait(false);
            return new LuaRuleExecutor(
                luaEngine,
                portrayal,
                new S101LuaDataProviderFactory(dataset, features),
                "S-101",
                S101ContextParameterBindings.Build());
        };
    }

    /// <summary>The alerts the features raise at the mariner settings.</summary>
    /// <param name="featureIds">The features' record ids, as strings.</param>
    /// <param name="mariner">
    /// The settings the rules see. A route check sets the safety contour and
    /// safety depth; the rest keep their defaults.
    /// </param>
    /// <param name="cancellationToken">Cancels the portrayal.</param>
    /// <returns>Each feature's alerts, distinct and in emission order; empty for a feature that raises none.</returns>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FeatureAlert>>> AlertsAsync(
        IReadOnlyCollection<string> featureIds, MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentNullException.ThrowIfNull(mariner);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is null || !Equals(_mariner, mariner))
            {
                _session?.Dispose();
                _session = null;
                _alerts.Clear();
                _executor ??= await _createExecutor(cancellationToken).ConfigureAwait(false);
                _session = await _executor.OpenSessionAsync(mariner, cancellationToken).ConfigureAwait(false);
                _mariner = mariner;
            }

            var missing = featureIds.Where(id => !_alerts.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var found = new Dictionary<string, List<FeatureAlert>>(StringComparer.Ordinal);
                foreach (var instruction in _session.Portray(missing))
                {
                    if (instruction.AlertReference is not { } alert)
                    {
                        continue;
                    }

                    var at = (instruction as PointInstruction)?.CoordinateOverride;
                    var alerts = found.TryGetValue(instruction.FeatureReference, out var list)
                        ? list
                        : found[instruction.FeatureReference] = [];
                    var featureAlert = new FeatureAlert(alert, at);
                    if (!alerts.Contains(featureAlert))
                    {
                        alerts.Add(featureAlert);
                    }
                }

                foreach (var id in missing)
                {
                    _alerts[id] = found.TryGetValue(id, out var alerts) ? alerts : [];
                }
            }

            return featureIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => _alerts[id], StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }
}
