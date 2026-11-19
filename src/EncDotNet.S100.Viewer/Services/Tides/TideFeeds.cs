using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Predicted tide and current stations as live feeds (data source design note
/// D8, slice 7d), in place of their Layer stack toggles: each is a Datasets
/// entry subscribed to in the Library's Live feeds. SoundCharts builds them,
/// since they follow its view time, viewport and loaded S-104 and S-111
/// (<see cref="TideStationsOverlay"/>).
/// </summary>
/// <remarks>
/// The processors don't own the sources: the timeline's tide lane, the legend
/// and the MCP tools read the same ones. <see cref="TideFeedHostExtension"/>
/// turns a source off when its entry is removed.
/// </remarks>
internal static class TideFeeds
{
    /// <summary>The tide stations' registration id.</summary>
    public const string TideStationsId = "tides.stations.predicted";

    /// <summary>The current stations' registration id.</summary>
    public const string CurrentStationsId = "tides.currents.predicted";

    /// <summary>The tide stations' registration.</summary>
    /// <param name="overlay">The sources and their bindings, resolved when the feed is first created.</param>
    public static SourceRegistration TideStations(Func<TideStationsOverlay> overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return new SourceRegistration
        {
            Id = TideStationsId,
            DisplayName = Strings.Tides_StationsLayer,
            Description = Strings.Tides_StationsLayer_Description,
            Spec = SpecName.TidePredictions,
            Group = SourceGroup.Predictions,
            Create = _ => new DynamicSourceProcessor(
                overlay().Source,
                TideStationPortrayal.Instance,
                new SpecRef(SpecName.TidePredictions, default),
                ownsSource: false),
        };
    }

    /// <summary>The current stations' registration.</summary>
    /// <param name="overlay">The sources and their bindings, resolved when the feed is first created.</param>
    public static SourceRegistration CurrentStations(Func<TideStationsOverlay> overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return new SourceRegistration
        {
            Id = CurrentStationsId,
            DisplayName = Strings.Tides_CurrentsLayer,
            Description = Strings.Tides_CurrentsLayer_Description,
            Spec = SpecName.TidePredictions,
            Group = SourceGroup.Predictions,
            Create = _ => new DynamicSourceProcessor(
                overlay().Currents,
                CurrentStationPortrayal.Instance,
                new SpecRef(SpecName.TidePredictions, default),
                ownsSource: false),
        };
    }
}

/// <summary>
/// Turns a predicted tide or current station source off when its Datasets
/// entry goes, since its processor doesn't own it (<see cref="TideFeeds"/>).
/// </summary>
/// <param name="sourceId">The registration id.</param>
/// <param name="source">
/// The source, resolved when an entry loads: the overlay depends on the
/// loaded datasets, which depend on the source host, so it can't be built
/// with the host.
/// </param>
internal sealed class TideFeedHostExtension(string sourceId, Func<EncDotNet.S100.DynamicSources.IPausableDynamicSource> source) : ISourceHostExtension
{
    /// <inheritdoc />
    public string SourceId => sourceId;

    /// <inheritdoc />
    public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor) =>
        new Detach(source());

    private sealed class Detach(EncDotNet.S100.DynamicSources.IPausableDynamicSource source) : IDisposable
    {
        public void Dispose() => source.IsActive = false;
    }
}
