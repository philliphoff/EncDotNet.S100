using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Default <see cref="IDynamicSourcePickService"/> implementation: hit-tests
/// the overlay sources (<see cref="IS100DynamicSourceRegistry.HitTest"/>),
/// which since slice 7d are only SoundCharts' own-ship, and makes each hit an
/// ordinary <see cref="PickHit"/> described as a source's processor would
/// describe it (data source design note P5): title, rows, a vessel section and
/// any pick actions. The hit has no dataset.
/// </summary>
internal sealed class DynamicSourcePickService : IDynamicSourcePickService
{
    private readonly IS100DynamicSourceRegistry _registry;

    /// <summary>Creates the service.</summary>
    /// <param name="registry">The overlay sources to hit-test.</param>
    public DynamicSourcePickService(IS100DynamicSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<PickHit> Pick(MPoint mapPoint, double resolution)
    {
        ArgumentNullException.ThrowIfNull(mapPoint);

        var raw = _registry.HitTest(mapPoint, resolution);
        if (raw.Count == 0)
            return [];
        return [.. raw.Select(ToPickHit)];
    }

    /// <summary>The pick hit of an overlay source's feature.</summary>
    /// <param name="hit">The hit.</param>
    internal static PickHit ToPickHit(DynamicSourceHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var info = DynamicSourceProcessor.Describe(hit.Source, hit.Feature, CultureInfo.CurrentCulture);
        var context = new PickContext
        {
            // An overlay source has no product of its own; own-ship's position
            // and motion come from the ship's instruments.
            Spec = new SpecRef(SpecName.Nmea, default),
            FeatureRef = info.FeatureRef,
            FeatureType = info.FeatureType,
            Attributes = info.Attributes,
            Sections = info.Sections,
        };
        return new PickHit
        {
            FeatureType = info.FeatureType,
            FeatureTypeName = info.FeatureTypeName,
            FeatureRef = info.FeatureRef,
            Attributes = info.Attributes,
            Geometry = info.Geometry,
            Sections = info.Sections,
            Actions = PickActionRegistry.Default.ActionsFor(context),
        };
    }
}
