using System.Text.Json;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Answers "what is here?" for the datasets a <see cref="RenderedTileSource"/>
/// serves, as <c>s100 identify</c> does: the vector features at a point and
/// the coverage values there. Backs WMS GetFeatureInfo.
/// </summary>
/// <remarks>
/// The datasets are opened into a headless catalogue on the first query, not
/// when serving starts, so a server nobody queries doesn't load them twice.
/// A source opened again after its files change gets a new query, so answers
/// follow the data served. Thread-safe.
/// </remarks>
internal sealed class DatasetFeatureQuery : IDisposable
{
    private readonly Lazy<Opened> _opened;

    /// <param name="settings">The inputs the tiles are rendered from.</param>
    public DatasetFeatureQuery(TilesRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _opened = new Lazy<Opened>(() => Open(settings), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The features and coverage values at <paramref name="latitude"/>,
    /// <paramref name="longitude"/> (WGS-84 degrees), with point and curve
    /// features matched within <paramref name="radiusMeters"/> and at most
    /// <paramref name="maxResults"/> features, each with its attributes.
    /// </summary>
    public Task<FeatureQueryResult> QueryAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        int maxResults,
        DateTime? time,
        CancellationToken cancellationToken) =>
        Task.Run(() => Query(latitude, longitude, radiusMeters, maxResults, time), cancellationToken);

    public void Dispose()
    {
        if (_opened.IsValueCreated)
            _opened.Value.Resolution?.Dispose();
    }

    private FeatureQueryResult Query(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time)
    {
        var opened = _opened.Value;
        if (opened.Catalog is not { } catalog)
            return new FeatureQueryResult(latitude, longitude, [], [], [], opened.Warnings);

        DateTimeOffset? at = time is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;
        var identified = new IdentifyFeaturesService(catalog).InvokeAsync(new IdentifyFeaturesRequest(
            latitude, longitude, Spec: null,
            Math.Clamp(radiusMeters, 0.0, 100_000.0),
            Math.Clamp(maxResults, 1, 200),
            at)).GetAwaiter().GetResult();
        if (!identified.TryGetValue(out var features))
        {
            identified.TryGetError(out var error);
            throw new InvalidOperationException(error?.Message ?? "The datasets couldn't be queried.");
        }

        var describer = new DescribeFeatureService(catalog);
        var matches = features.Features
            .Select(f => new FeatureQueryMatch(f, IdentifyCommand.TryDescribeAttributes(describer, f.DatasetId, f.FeatureId)))
            .ToList();
        var samples = IdentifyCommand.SampleCoverages(catalog, latitude, longitude, at, specFilter: null);
        return new FeatureQueryResult(
            latitude, longitude, matches, samples, IdentifyCommand.Notices(features, samples), opened.Warnings);
    }

    private static Opened Open(TilesRenderSettings settings)
    {
        var warnings = new List<string>();
        var inputs = DatasetInputResolver.Resolve(
            settings.IsComposite || settings.IsExplicitExchangeSet ? null : settings.Input,
            settings.Layers,
            settings.IsExplicitExchangeSet ? settings.ExchangeSet : null,
            settings.Only,
            warnings,
            out var resolution);
        if (inputs.Count == 0)
            return new Opened(null, resolution, warnings);

        var catalog = FileDatasetCatalog.Build(inputs, new ProjNetCrsTransformFactory());
        warnings.AddRange(catalog.Warnings);
        return new Opened(catalog, resolution, warnings);
    }

    private sealed record Opened(FileDatasetCatalog? Catalog, IDisposable? Resolution, IReadOnlyList<string> Warnings);
}

/// <summary>A feature found by <see cref="DatasetFeatureQuery"/>, with its attributes when the product describes them.</summary>
internal sealed record FeatureQueryMatch(IdentifyMatch Match, JsonElement? Attributes);

/// <summary>What <see cref="DatasetFeatureQuery"/> found at a point.</summary>
internal sealed record FeatureQueryResult(
    double Latitude,
    double Longitude,
    IReadOnlyList<FeatureQueryMatch> Features,
    IReadOnlyList<IdentifyCommand.CoverageSample> Samples,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Warnings);
