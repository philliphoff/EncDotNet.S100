using System.Collections.Specialized;
using System.ComponentModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Services.Aml;

/// <summary>
/// Outlines the coverage of AML cells that aren't drawn, or whose drawing is
/// hidden (#1371, decision 9), so the map shows where the military data is.
/// </summary>
/// <remarks>
/// <para>
/// An outline follows its row's <see cref="DatasetEntry.ShowsAmlCoverageOutline"/>:
/// the row's eye turns it off, and a cell the AML portrayal draws has none
/// unless its layers are hidden. The coverage is the cell's <c>M_COVR</c>
/// areas, or its bounding box when it has none.
/// </para>
/// <para>
/// The outline is chart-overlay ink, not a tool: it isn't pickable (taps go
/// through to the chart and the AML features below it), and it carries no
/// hatch or tint, so it can't be read as protected data or Library coverage.
/// </para>
/// </remarks>
internal sealed class AmlCoverageOutlineController : IDisposable
{
    /// <summary>The overlay layer's name.</summary>
    internal const string LayerName = "AML Coverage Outlines";

    private static readonly SKColor DayInk = new(0x27, 0x27, 0x2A);
    private static readonly SKColor DuskInk = new(0xC8, 0xC8, 0xCC);
    private static readonly SKColor NightInk = new(0x8A, 0x8A, 0x90);

    private readonly IMapLayerCollection _layers;
    private readonly DatasetsViewModel _datasets;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly Action<Action> _marshal;
    private readonly Action<Action> _coalesce;
    private readonly MemoryLayer _layer;
    private readonly HashSet<DatasetEntry> _subscribed = [];
    private IReadOnlyList<AmlCoverageOutline> _outlines = [];
    private bool _rebuildPending;
    private bool _disposed;

    /// <summary>Creates the controller and adds its layer to the overlay band.</summary>
    /// <param name="layers">The map's layer bands.</param>
    /// <param name="datasets">The Datasets panel, whose AML rows are outlined.</param>
    /// <param name="appearance">The chart palette, which picks the ink.</param>
    /// <param name="marshal">Runs work on the UI thread (the app passes <see cref="UiThread.RunOrPost"/>).</param>
    /// <param name="coalesce">Queues a rebuild on the UI thread, even from it (the app passes <see cref="UiThread.Post"/>).</param>
    public AmlCoverageOutlineController(
        IMapLayerCollection layers,
        DatasetsViewModel datasets,
        IMeasureOverlayAppearanceProvider appearance,
        Action<Action> marshal,
        Action<Action> coalesce)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(coalesce);

        AmlCoverageOutlineRenderer.Register();
        _layers = layers;
        _datasets = datasets;
        _appearance = appearance;
        _marshal = marshal;
        _coalesce = coalesce;
        _layer = new WorldCopyMemoryLayer
        {
            Name = LayerName,
            Style = null,
            Features = new List<IFeature>(),
        };

        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
        _appearance.Changed += OnAppearanceChanged;

        _marshal(() =>
        {
            _layers.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    /// <summary>The outlines the map shows, in panel order (for MCP and tests).</summary>
    public IReadOnlyList<AmlCoverageOutline> Outlines => _outlines;

    /// <summary>The overlay layer (for tests).</summary>
    internal ILayer Layer => _layer;

    /// <summary>The label card's lines for <paramref name="entry"/>: "AML · CLB  NOC6RA3B" and "Not drawn · RESTRICTED".</summary>
    internal static (string Title, string Subtitle) LabelOf(DatasetEntry entry)
    {
        var aml = entry.Aml;
        var title = $"{entry.AmlTagText}  {entry.DisplayName}";
        var state = aml?.Drawn == true ? Strings.Aml_Outline_Hidden : Strings.Aml_Outline_NotDrawn;
        var subtitle = entry.AmlMarking is { } marking ? $"{state} · {marking}" : state;
        return (title, subtitle);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DatasetEntry entry in e.OldItems)
                Unsubscribe(entry);
        }

        if (e.NewItems is not null)
        {
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed)
                entry.PropertyChanged -= OnEntryChanged;
            _subscribed.Clear();
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }

        ScheduleRebuild();
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        if (_subscribed.Remove(entry))
            entry.PropertyChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.ShowsAmlCoverageOutline) or nameof(DatasetEntry.Aml))
            ScheduleRebuild();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (_rebuildPending)
            return;
        _rebuildPending = true;
        _coalesce(() =>
        {
            _rebuildPending = false;
            Rebuild();
        });
    }

    /// <summary>Redraws every outline now (tests call it instead of waiting for the coalesced rebuild).</summary>
    internal void Rebuild()
    {
        if (_disposed)
            return;

        var appearance = _appearance.Current;
        var ink = appearance.Palette switch
        {
            PaletteType.Night => NightInk,
            PaletteType.Dusk => DuskInk,
            _ => DayInk,
        };
        // Paper on Day (white, as the design draws it), the palette's black on Dusk and Night.
        var card = appearance.Palette == PaletteType.Day ? SKColors.White : SKColors.Black;

        var outlines = new List<AmlCoverageOutline>();
        var features = new List<IFeature>();
        foreach (var entry in _datasets.Entries)
        {
            if (!entry.ShowsAmlCoverageOutline || entry.Aml is not { } aml || ToMercator(aml.Coverage) is not { } geometry)
                continue;

            var (title, subtitle) = LabelOf(entry);
            var feature = new GeometryFeature(geometry);
            feature.Styles.Add(new AmlCoverageOutlineStyle { Ink = ink, Card = card, Title = title, Subtitle = subtitle });
            features.Add(feature);
            outlines.Add(new AmlCoverageOutline(entry.Id.Value, title, subtitle));
        }

        _outlines = outlines;
        _layer.Features = features;
        _layer.DataHasChanged();
    }

    private static Geometry? ToMercator(IReadOnlyList<DataModel.CoverageArea> coverage)
    {
        var factory = NetTopologySuite.Geometries.GeometryFactory.Default;
        var polygons = new List<Polygon>();
        foreach (var area in coverage)
        {
            if (ToRing(factory, area.ExteriorRing) is not { } shell)
                continue;
            var holes = area.InteriorRings.Select(r => ToRing(factory, r)).OfType<LinearRing>().ToArray();
            polygons.Add(factory.CreatePolygon(shell, holes));
        }

        return polygons.Count switch
        {
            0 => null,
            1 => polygons[0],
            _ => factory.CreateMultiPolygon(polygons.ToArray()),
        };
    }

    private static LinearRing? ToRing(GeometryFactory factory, IReadOnlyList<DataModel.GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(ring.Count + 1);
        foreach (var p in ring)
        {
            var latitude = Math.Clamp(p.Latitude, -85.05112878, 85.05112878);
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(p.Longitude, latitude);
            coordinates.Add(new Coordinate(x, y));
        }

        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());
        return coordinates.Count >= 4 ? factory.CreateLinearRing(coordinates.ToArray()) : null;
    }

    /// <summary>Removes the layer and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _subscribed)
            entry.PropertyChanged -= OnEntryChanged;
        _subscribed.Clear();
        _appearance.Changed -= OnAppearanceChanged;

        _marshal(() => _layers.RemoveOverlayLayer(_layer));
    }
}

/// <summary>One AML coverage outline on the map.</summary>
/// <param name="DatasetId">The dataset's id.</param>
/// <param name="Title">The label card's first line.</param>
/// <param name="Subtitle">The label card's second line.</param>
internal sealed record AmlCoverageOutline(string DatasetId, string Title, string Subtitle);
