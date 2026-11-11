using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the Layer Stack panel (PR-L3). Subscribes to
/// <see cref="IDatasetLoaderService.LayerStackChanged"/> and reflects
/// the rich <see cref="LayerStackEntry"/> snapshot from
/// <see cref="IDatasetLoaderService.CurrentStackEntries"/> as a list
/// of <see cref="LayerStackPlaneViewModel"/> groups — one per
/// <see cref="S98DisplayPlane"/> — ordered top-of-stack first
/// (S-98 §A-3.2.1.1 + MSC.530(106)/Rev.1 §Appendix 2).
/// </summary>
/// <remarks>
/// <para>
/// Empty planes (those with no entries) are hidden by default. The
/// user can flip the <see cref="ShowEmptyPlanes"/> toggle to see
/// every canonical S-98 plane regardless.
/// </para>
/// <para>
/// Per-plane expansion state is preserved across rebuilds keyed by
/// the plane enum value so a load/remove/active-toggle doesn't
/// collapse the user's previously expanded planes.
/// </para>
/// </remarks>
internal sealed class LayerStackViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;
    private readonly IS100DynamicSourceRegistry? _dynamicSources;
    private readonly ITimeFormatProvider? _timeFormat;
    // PR-L4 reserve: kept on the field so the constructor still
    // captures it. _ = is enough to suppress unused-field warnings.
    // The plan reserves ITimeFormatProvider for time-aware status
    // text on per-entry rows.

    /// <summary>Plane group order: top-of-paint-stack first.</summary>
    private static readonly S98DisplayPlane[] PlanesTopFirst =
    {
        S98DisplayPlane.EcdisAlerts,
        S98DisplayPlane.MarinerOverlay,
        S98DisplayPlane.DynamicArrows,
        S98DisplayPlane.CautionsAndWarnings,
        S98DisplayPlane.AdditionalMilitaryLayers,
        S98DisplayPlane.OtherChartOverlays,
        S98DisplayPlane.BaseChartOver,
        S98DisplayPlane.OnDemandSurface,
        S98DisplayPlane.Bathymetry,
        S98DisplayPlane.BaseChartUnder,
    };

    public ObservableCollection<LayerStackPlaneViewModel> Planes { get; } = new();

    private readonly Dictionary<S98DisplayPlane, bool> _planeExpansion = new();

    private bool _showEmptyPlanes;
    /// <summary>
    /// When true the panel shows every canonical S-98 display plane
    /// even if no loaded dataset contributes layers to it. Default
    /// false to keep the tree compact for typical single-product
    /// sessions.
    /// </summary>
    public bool ShowEmptyPlanes
    {
        get => _showEmptyPlanes;
        set
        {
            if (SetProperty(ref _showEmptyPlanes, value))
            {
                Rebuild();
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>True when no plane row is currently displayed.</summary>
    public bool IsEmpty => Planes.Count == 0;

    /// <summary>"Active vs. Visibility" help text for the panel footer.</summary>
    public string ActiveVsVisibilityHelp => Strings.LayerStack_VisibilityVsActive_HelpText;

    private readonly Func<string, string?>? _inactiveReason;

    public LayerStackViewModel(
        IDatasetLoaderService loader,
        IS100DynamicSourceRegistry? dynamicSources,
        ITimeFormatProvider? timeFormat,
        Services.DisplayCombinations.DisplayCombinationService? displayCombinations,
        DatasetsViewModel? datasets)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        if (displayCombinations is not null && datasets is not null)
        {
            // "Not in Pilotage" on the Active toggle of a dataset the active
            // display combination left out (#1192).
            _inactiveReason = id => datasets.Entries.FirstOrDefault(e => e.Id.Value == id) is { } entry
                ? displayCombinations.InactiveReason(entry)
                : null;
        }
        _dynamicSources = dynamicSources;
        _timeFormat = timeFormat;
        _ = _timeFormat; // reserved for PR-L4 per-entry timestamp display
        _loader.LayerStackChanged += OnLayerStackChanged;
        if (_dynamicSources is not null)
            _dynamicSources.SourcesChanged += OnLayerStackChanged;
        Rebuild();
    }

    private void OnLayerStackChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Rebuild();
        else
            Dispatcher.UIThread.Post(Rebuild);
    }

    /// <summary>
    /// Rebuilds the plane tree from the loader's current
    /// <see cref="LayerStackEntry"/> snapshot plus any registered
    /// dynamic feature sources (PR-D2.1). Preserves
    /// <see cref="LayerStackPlaneViewModel.IsExpanded"/> per plane
    /// across rebuilds via <see cref="_planeExpansion"/>.
    /// </summary>
    public void Rebuild()
    {
        // Snapshot expansion state from existing plane VMs so it
        // survives the collection reset.
        foreach (var existing in Planes)
            _planeExpansion[existing.Plane] = existing.IsExpanded;

        Planes.Clear();

        var entries = _loader.CurrentStackEntries;
        // Group by plane preserving entry order (bottom-of-paint-stack
        // first → within-plane priority asc, which is the order the
        // authority hands them back).
        var byPlane = new Dictionary<S98DisplayPlane, List<LayerStackEntry>>();
        foreach (var e in entries)
        {
            if (!byPlane.TryGetValue(e.Plane, out var list))
            {
                list = new List<LayerStackEntry>();
                byPlane[e.Plane] = list;
            }
            list.Add(e);
        }

        // PR-D2.1: dynamic feature sources land in their metadata's
        // plane (#1070). Snapshot once here so the
        // bucket sees a stable view even if the registry mutates
        // mid-rebuild.
        var dynamicSources = _dynamicSources?.Sources ?? Array.Empty<DynamicSourceRegistrationInfo>();

        foreach (var plane in PlanesTopFirst)
        {
            byPlane.TryGetValue(plane, out var planeEntries);
            var planeEntriesList = planeEntries ?? new List<LayerStackEntry>();
            // Each source sits in the plane its metadata names (DynamicArrows
            // by default; NDBC observations on OtherChartOverlays), highest
            // within-plane priority first, then registration order.
            var dynamicForPlane = dynamicSources
                .Where(info => info.Plane == plane)
                .OrderByDescending(info => info.WithinPlanePriority)
                .ToList();

            if (planeEntriesList.Count == 0 && dynamicForPlane.Count == 0 && !_showEmptyPlanes)
                continue;

            // Dataset rows: highest WithinPlanePriority first so the
            // tree reads like the paint stack — top wins.
            var datasetChildren = planeEntriesList
                .OrderByDescending(e => e.WithinPlanePriority)
                .ThenBy(e => e.SourceDatasetId, StringComparer.Ordinal)
                .Select(e => (ViewModelBase)new LayerStackEntryViewModel(_loader, e, _inactiveReason));

            // Dynamic rows: placed below the dataset rows in the panel.
            var dynamicChildren = dynamicForPlane
                .Select(info => (ViewModelBase)new LayerStackDynamicEntryViewModel(_dynamicSources!, info));

            var children = datasetChildren.Concat(dynamicChildren).ToList();

            var isExpanded = !_planeExpansion.TryGetValue(plane, out var prev) || prev;
            var vm = new LayerStackPlaneViewModel(plane, children, isExpanded);
            Planes.Add(vm);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// One <see cref="S98DisplayPlane"/> group row in the Layer Stack
/// panel — its title, contributing entries, expansion state, and
/// child count.
/// </summary>
internal sealed class LayerStackPlaneViewModel : ViewModelBase
{
    public S98DisplayPlane Plane { get; }
    public IReadOnlyList<ViewModelBase> Children { get; }

    public string Title => Plane switch
    {
        S98DisplayPlane.BaseChartUnder => Strings.LayerStack_Plane_BaseChartUnder,
        S98DisplayPlane.Bathymetry => Strings.LayerStack_Plane_Bathymetry,
        S98DisplayPlane.OnDemandSurface => Strings.LayerStack_Plane_OnDemandSurface,
        S98DisplayPlane.BaseChartOver => Strings.LayerStack_Plane_BaseChartOver,
        S98DisplayPlane.OtherChartOverlays => Strings.LayerStack_Plane_OtherChartOverlays,
        S98DisplayPlane.AdditionalMilitaryLayers => Strings.LayerStack_Plane_AdditionalMilitaryLayers,
        S98DisplayPlane.CautionsAndWarnings => Strings.LayerStack_Plane_CautionsAndWarnings,
        S98DisplayPlane.DynamicArrows => Strings.LayerStack_Plane_DynamicArrows,
        S98DisplayPlane.MarinerOverlay => Strings.LayerStack_Plane_MarinerOverlay,
        S98DisplayPlane.EcdisAlerts => Strings.LayerStack_Plane_EcdisAlerts,
        _ => Plane.ToString(),
    };

    public int ChildCount => Children.Count;

    public string ChildCountText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.LayerStack_ChildCountFormat, ChildCount);

    public bool IsEmpty => Children.Count == 0;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public LayerStackPlaneViewModel(
        S98DisplayPlane plane,
        IReadOnlyList<ViewModelBase> children,
        bool isExpanded)
    {
        Plane = plane;
        Children = children;
        _isExpanded = isExpanded;
    }
}

/// <summary>
/// One dynamic-feature-source child row in the Layer Stack panel
/// (PR-D2.1) — its source id, display name, optional description,
/// and a two-way bound <see cref="IsActive"/> checkbox routed
/// through <see cref="IS100DynamicSourceRegistry.SetVisible"/>.
/// </summary>
/// <remarks>
/// A sibling of <see cref="LayerStackEntryViewModel"/>: both inherit
/// <see cref="ViewModelBase"/> so the plane VM can hold a mixed
/// collection. XAML picks the appropriate template by runtime type
/// via <c>ItemsControl.DataTemplates</c>.
/// </remarks>
internal sealed class LayerStackDynamicEntryViewModel : ViewModelBase
{
    private readonly IS100DynamicSourceRegistry _registry;

    public string SourceId { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>
    /// The visibility checkbox's automation id,
    /// <c>LayerStack.DynamicEntry.{SourceId}.Active</c>, unique per source.
    /// </summary>
    public string AutomationId => $"LayerStack.DynamicEntry.{SourceId}.Active";

    /// <summary>
    /// Whether the source's overlay layer is currently visible.
    /// Routes through
    /// <see cref="IS100DynamicSourceRegistry.SetVisible"/>; the
    /// registry's resulting <c>SourcesChanged</c> event triggers a
    /// VM rebuild so the new state surfaces immediately.
    /// </summary>
    public bool IsActive
    {
        get => _registry.GetVisible(SourceId);
        set
        {
            if (_registry.GetVisible(SourceId) == value) return;
            _registry.SetVisible(SourceId, value);
            OnPropertyChanged();
        }
    }

    public LayerStackDynamicEntryViewModel(
        IS100DynamicSourceRegistry registry,
        DynamicSourceRegistrationInfo info)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(info);
        _registry = registry;
        SourceId = info.Id;
        DisplayName = info.DisplayName;
        Description = info.Description;
    }
}

/// <summary>
/// One <see cref="LayerStackEntry"/> child row — its source
/// dataset id, optional source feature type, within-plane priority,
/// and a two-way bound <see cref="IsActive"/> checkbox routed
/// through <see cref="IDatasetLoaderService.SetActive"/>.
/// </summary>
internal sealed class LayerStackEntryViewModel : ViewModelBase
{
    private readonly IDatasetLoaderService _loader;
    public LayerStackEntry Entry { get; }

    public string DatasetId => Entry.SourceDatasetId;

    public string DatasetLabel
    {
        get
        {
            // Show just the file name for typical file paths to keep
            // the tree narrow; tooltip carries the full id.
            try { return System.IO.Path.GetFileName(Entry.SourceDatasetId) is { Length: > 0 } n ? n : Entry.SourceDatasetId; }
            catch { return Entry.SourceDatasetId; }
        }
    }

    public string? SourceFeatureType => Entry.SourceFeatureType;

    public bool HasSourceFeatureType => !string.IsNullOrEmpty(Entry.SourceFeatureType);

    public int WithinPlanePriority => Entry.WithinPlanePriority;

    public string PriorityText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.LayerStack_PriorityFormat, Entry.WithinPlanePriority);

    public bool IsActive
    {
        get => _loader.GetActive(Entry.SourceDatasetId);
        set
        {
            if (_loader.GetActive(Entry.SourceDatasetId) == value) return;
            _loader.SetActive(Entry.SourceDatasetId, value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The legend of a sub-layer that carries one, row by row: the colour
    /// bands of a weather fill (<see cref="CoverageSubLayerBase.Legend"/>,
    /// #1072), or the symbol, line and area rows of a vector overlay
    /// (<see cref="VectorSubLayer.Legend"/>, #1293); empty otherwise. The
    /// colours are those of the palette the sub-layer was last drawn in.
    /// </summary>
    public IReadOnlyList<LayerStackLegendItemViewModel> Legend { get; }

    /// <summary>Whether the entry shows a legend.</summary>
    public bool HasLegend => Legend.Count > 0;

    /// <summary>
    /// The legend's accessible name, e.g. "Legend of Wave height: gfswave.grib2,
    /// 15 bands" for a colour fill, or "Legend of Fronts: codsus_hr.txt, 5
    /// items" when it has symbol or line rows, from the sub-layer's name.
    /// </summary>
    public string LegendName => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Legend.All(row => row.IsSwatch) ? Strings.LayerStack_LegendNameFormat : Strings.LayerStack_LegendRowsNameFormat,
        Entry.Item.Payload switch
        {
            CoverageStackPayload coverage => coverage.SubLayer.LayerName,
            VectorStackPayload vector => vector.SubLayer.LayerName,
            _ => DatasetLabel,
        },
        Legend.Count);

    public LayerStackEntryViewModel(
        IDatasetLoaderService loader,
        LayerStackEntry entry,
        Func<string, string?>? inactiveReason)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(entry);
        _loader = loader;
        Entry = entry;
        Legend = LegendOf(entry);
        _inactiveReason = inactiveReason;
    }

    private readonly Func<string, string?>? _inactiveReason;

    /// <summary>
    /// The Active toggle's tooltip: why the dataset is inactive when a display
    /// combination left it out ("Not in Pilotage"), else what Active means.
    /// </summary>
    public string ActiveTooltip =>
        _inactiveReason?.Invoke(Entry.SourceDatasetId) ?? Strings.LayerStack_ActiveTooltip;

    private static IReadOnlyList<LayerStackLegendItemViewModel> LegendOf(LayerStackEntry entry)
    {
        var legend = entry.Item.Payload switch
        {
            CoverageStackPayload { SubLayer: GridCoverageSubLayer grid } => grid.Legend,
            VectorStackPayload vector => vector.SubLayer.Legend,
            _ => null,
        };
        return legend is { Count: > 0 }
            ? legend.Select(e => new LayerStackLegendItemViewModel(e)).ToArray()
            : [];
    }
}

/// <summary>
/// One row of a layer stack entry's legend: a glyph and its label. A plain
/// colour band shows as a swatch (<see cref="Swatch"/>); an outlined area, a
/// symbol or a line sample as a picture drawn by
/// <see cref="Renderers.Skia.LegendGlyphRenderer"/> (<see cref="Glyph"/>).
/// </summary>
internal sealed class LayerStackLegendItemViewModel : ViewModelBase
{
    /// <summary>The device scale glyphs are drawn at, so they stay sharp on a high-density display.</summary>
    private const float GlyphDeviceScale = 2f;

    private readonly LegendEntry _entry;
    private Avalonia.Media.Imaging.Bitmap? _glyph;

    /// <summary>Creates a legend row.</summary>
    /// <param name="entry">The legend row of the sub-layer.</param>
    public LayerStackLegendItemViewModel(LegendEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Label);
        _entry = entry;
        Label = entry.Label;
        Kind = LegendRows.KindOf(entry);
        Color = entry is SwatchLegendEntry swatch ? swatch.Color : null;
        Swatch = Color is not null && Avalonia.Media.Color.TryParse(Color, out var parsed)
            ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(parsed)
            : Brushes.Transparent;
        (GlyphWidth, GlyphHeight) = Renderers.Skia.LegendGlyphRenderer.SizeOf(entry);
    }

    /// <summary>What the row's glyph shows.</summary>
    public LegendRowKind Kind { get; }

    /// <summary>The swatch colour as a hex string, or <see langword="null"/> for a symbol or line row.</summary>
    public string? Color { get; }

    /// <summary>The row label; also the row's accessible name.</summary>
    public string Label { get; }

    /// <summary>The swatch brush; transparent for a row without a colour.</summary>
    public IBrush Swatch { get; }

    /// <summary>Whether the row is a plain colour swatch, drawn as a bordered box.</summary>
    public bool IsSwatch => Kind == LegendRowKind.Swatch;

    /// <summary>Whether the row shows a drawn glyph: an area, a symbol or a line sample.</summary>
    public bool HasGlyph => !IsSwatch;

    /// <summary>The glyph's width in logical pixels.</summary>
    public int GlyphWidth { get; }

    /// <summary>The glyph's height in logical pixels.</summary>
    public int GlyphHeight { get; }

    /// <summary>
    /// The glyph picture of an area, symbol or line row, drawn on first use;
    /// <see langword="null"/> for a plain swatch.
    /// </summary>
    public Avalonia.Media.IImage? Glyph => IsSwatch ? null : _glyph ??= RenderGlyph(_entry);

    private static Avalonia.Media.Imaging.Bitmap RenderGlyph(LegendEntry entry)
    {
        using var bitmap = Renderers.Skia.LegendGlyphRenderer.Render(entry, GlyphDeviceScale);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}
