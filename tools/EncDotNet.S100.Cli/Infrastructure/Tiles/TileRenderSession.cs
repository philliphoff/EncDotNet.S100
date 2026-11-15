using System.Collections.Concurrent;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting;
using SkiaSharp;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>The EPSG:3857 area to tile; <c>MaxX</c> may lie past +180° for data crossing the antimeridian.</summary>
internal readonly record struct TileArea(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>
/// Where and how a tile set is rendered: the area, the zoom range, and the
/// latitude each zoom level's scale is measured at.
/// </summary>
internal sealed record TileLayout(TileArea Area, double ReferenceLatitude, int MinZoom, int MaxZoom, ChartTileOptions TileOptions)
{
    /// <summary>The WGS-84 bounds of the area, the full longitude range when it wraps the antimeridian.</summary>
    public (double West, double South, double East, double North) Bounds
    {
        get
        {
            var (west, south) = WebMercator.ToLonLat(Area.MinX, Area.MinY);
            var (east, north) = WebMercator.ToLonLat(Area.MaxX, Area.MaxY);
            if (Area.MaxX - Area.MinX >= WebMercator.Circumference || Area.MinX < -XyzTileGrid.Extent || Area.MaxX > XyzTileGrid.Extent)
                return (-180, south, 180, north);
            return (west, south, east, north);
        }
    }
}

/// <summary>
/// Datasets opened for tiling (issues #847, #865): the processors
/// <c>tiles export</c> and <c>tiles serve</c> portray, and the
/// <see cref="TileScene"/> each palette is rendered from.
/// </summary>
internal sealed class TileRenderSession : IDisposable
{
    /// <summary>The deepest zoom a default (derived) zoom range reaches.</summary>
    private const int DefaultZoomCeiling = 18;

    private readonly TilesRenderSettings _settings;
    private readonly DatasetChanges? _changes;
    private readonly IDisposable _catalogueManager;
    private readonly IDisposable? _resolution;
    private readonly List<IDatasetProcessor> _processors;

    // Portrayal is serialised: the processors are shared by every palette's
    // scene and every zoom level's coverage portrayal.
    private readonly Lock _portrayGate = new();

    // The portrayal of each dataset that doesn't vary with time, per palette:
    // reused by every time step's scene.
    // Keyed by the mariner settings too (MarinerStyle.Key), as they change the portrayal.
    private readonly Dictionary<(PaletteType Palette, string Mariner, int Index), VectorPortrayalResult> _timelessPortrayals = [];

    private TileRenderSession(
        TilesRenderSettings settings,
        IDisposable catalogueManager,
        IDisposable? resolution,
        List<IDatasetProcessor> processors,
        IReadOnlyList<string> specs,
        IReadOnlyList<string>? names = null,
        IReadOnlyList<string>? paths = null,
        DatasetChanges? changes = null)
    {
        _settings = settings;
        _changes = changes;
        _catalogueManager = catalogueManager;
        _resolution = resolution;
        _processors = processors;
        Specs = specs;
        Names = names ?? specs;
        Paths = paths ?? Names;
        TryParseImage(settings, out var format);
        Format = format;
        Times = processors
            .OfType<ITimeAwareDatasetProcessor>()
            .SelectMany(p => p.AvailableTimes)
            .Select(AsUtc)
            .Distinct()
            .Order()
            .ToArray();
    }

    /// <summary>The product specification of each dataset, in input order.</summary>
    public IReadOnlyList<string> Specs { get; }

    /// <summary>The file name of each dataset, in input order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>The full path of each dataset, in input order.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>The opened datasets, in input order.</summary>
    internal IReadOnlyList<IDatasetProcessor> Processors => _processors;

    /// <summary>The tile encoding.</summary>
    public TileImageFormat Format { get; }

    /// <summary>
    /// Opens and checks the datasets <paramref name="settings"/> names. On
    /// failure, prints why and returns a non-zero exit code with
    /// <paramref name="session"/> <see langword="null"/>.
    /// </summary>
    /// <param name="settings">The command's settings.</param>
    /// <param name="session">The opened datasets, or <see langword="null"/> on failure.</param>
    /// <param name="changes">Datasets added to and removed from those <paramref name="settings"/> names while serving (issue #1494); <see langword="null"/> for none.</param>
    public static int TryOpen(TilesRenderSettings settings, out TileRenderSession? session, DatasetChanges? changes = null)
    {
        session = null;
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build(LuaEngineOption.Resolve(settings.LuaEngine));
        IDisposable? resolution = null;
        var processors = new List<IDatasetProcessor>();
        var specsOpened = new List<string>();
        var namesOpened = new List<string>();
        var pathsOpened = new List<string>();
        bool opened = false;
        try
        {
            IReadOnlyList<(string Path, string Spec, bool ApplyUpdates)> inputs;
            bool applyUpdates = !settings.NoUpdates;
            string? folder = settings.FolderSource;
            if (folder is not null)
            {
                // A folder of datasets, as s100 mcp serve reads one: loose cells keep
                // their sibling S-101 updates; cells in an S-100 exchange set don't.
                var warnings = new List<string>();
                var resolved = DatasetInputResolver.Resolve(folder, [], null, settings.Only, warnings, out resolution);
                foreach (var warning in warnings)
                    Console.Error.WriteLine(warning);
                inputs = resolved
                    .Select(i => (i.Path, i.Spec, applyUpdates && !IsInsideExchangeSet(i.Path, folder)))
                    .ToList();
            }
            else if (settings.IsExchangeSet)
            {
                IReadOnlySet<string>? only = null;
                if (settings.Only is not null && RenderCommand.TryParseOnlySpecs(settings.Only, out var specs))
                    only = specs;

                var exchangeSet = ExchangeSetLayerResolution.Resolve(settings.ExchangeSetSource!, only);
                resolution = exchangeSet;
                foreach (var warning in exchangeSet.Warnings)
                    Console.Error.WriteLine(warning);
                inputs = exchangeSet.Layers.Select(l => (l.Path, l.Spec, false)).ToList();
            }
            else
            {
                var paths = settings.IsComposite ? settings.Layers : [settings.Input!];
                var resolved = new List<(string, string, bool)>();
                foreach (var path in paths)
                {
                    var spec = DatasetPipelineFactory.DetectProductSpec(path);
                    if (spec is null)
                    {
                        UnrecognizedDatasetMessage.Write(path);
                        return 2;
                    }

                    resolved.Add((path, spec, applyUpdates));
                }

                inputs = resolved;
            }

            if (changes is { IsEmpty: false } && !changes.TryApply(ref inputs, applyUpdates))
                return 2;

            if (inputs.Count == 0)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]No renderable datasets were found in:[/] {folder ?? settings.ExchangeSetSource}");
                return 2;
            }

            if (!string.IsNullOrWhiteSpace(settings.DisplayMode)
                && !inputs.Any(i => i.Spec.Equals("S-411", StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine("[red]--display-mode is only supported for S-411 sea-ice datasets; none of the inputs is S-411.[/]");
                return 2;
            }

            // With --bbox, a dataset wholly outside the area its tiles cover is
            // closed before it is portrayed: in a large folder most datasets are,
            // and their portrayals would hold most of the memory (#1464).
            double[]? box = settings.BoundingBox is not null
                && CompositeViewportBuilder.TryParseDoubles(settings.BoundingBox, 4, out var parsedBox)
                    ? parsedBox
                    : null;

            foreach (var (path, spec, applyInputUpdates) in inputs)
            {
                IDatasetProcessor processor;
                try
                {
                    processor = DatasetProcessorLoader.Create(factory, spec, path, noUpdates: !applyInputUpdates);
                }
                catch (Exception e) when (folder is not null && e is not OperationCanceledException)
                {
                    // In a folder, one unreadable dataset doesn't stop the rest.
                    Console.Error.WriteLine($"Skipped {spec} dataset that could not be opened: {path} ({e.Message})");
                    continue;
                }

                if (processor.VersionAssessment?.IsWarning == true)
                    Console.Error.WriteLine(processor.VersionAssessment.BuildMessage());
                if (processor is not (IVectorPortrayalSource or ICoveragePortrayalSource))
                {
                    (processor as IDisposable)?.Dispose();
                    if (folder is not null)
                    {
                        Console.Error.WriteLine($"Skipped {spec} dataset, which can't be tiled: {path}");
                        continue;
                    }

                    AnsiConsole.MarkupLineInterpolated($"[red]Tiling is not supported for {spec}:[/] {path}");
                    return 3;
                }

                processors.Add(processor);
                specsOpened.Add(spec);
                namesOpened.Add(Path.GetFileName(path));
                pathsOpened.Add(Path.GetFullPath(path));
            }

            int outside = 0;
            if (box is not null)
            {
                var filter = FilterBox(box, settings.MinZoom ?? LowestDefaultZoom(processors, box));
                var crs = new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory();
                for (int i = processors.Count - 1; i >= 0; i--)
                {
                    if (!IsOutside(processors[i], filter, crs))
                        continue;

                    (processors[i] as IDisposable)?.Dispose();
                    processors.RemoveAt(i);
                    specsOpened.RemoveAt(i);
                    namesOpened.RemoveAt(i);
                    pathsOpened.RemoveAt(i);
                    outside++;
                }
            }

            if (outside > 0)
                Console.Error.WriteLine($"Left out {outside} dataset(s) outside --bbox.");

            if (processors.Count == 0 && outside > 0)
            {
                AnsiConsole.MarkupLine("[red]No dataset covers --bbox.[/]");
                return 2;
            }

            if (processors.Count == 0)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]No datasets that can be tiled were found in:[/] {folder ?? settings.ExchangeSetSource}");
                return 2;
            }

            session = new TileRenderSession(settings, catalogueManager, resolution, processors, specsOpened, namesOpened, pathsOpened, changes);
            opened = true;
            return 0;
        }
        finally
        {
            if (!opened)
            {
                foreach (var processor in processors)
                    (processor as IDisposable)?.Dispose();
                resolution?.Dispose();
                catalogueManager.Dispose();
            }
        }
    }

    /// <summary>
    /// The WGS-84 box a dataset must reach to draw in a tile that covers
    /// <paramref name="box"/>: the box grown to whole tiles, and one more ring
    /// of tiles for symbols and text drawn past a dataset's extent, at the
    /// lowest zoom written. Without <c>--min-zoom</c> that zoom isn't known
    /// until the datasets are portrayed, so two levels below the zoom at which
    /// the box fits in one tile stands in for it.
    /// </summary>
    internal static double[] FilterBox(double[] box, int? minZoom)
    {
        if (box[0] > box[2] || box[0] < -180 || box[2] > 180)
            return box;

        double west = box[0], south = box[1], east = box[2], north = box[3];
        if (west > east || west < -180 || east > 180)
            return box;

        var (minX, minY) = WebMercator.FromLonLat(west, Math.Max(south, -WebMercator.MaxLatitude));
        var (maxX, maxY) = WebMercator.FromLonLat(east, Math.Min(north, WebMercator.MaxLatitude));
        double span = Math.Max(maxX - minX, maxY - minY);
        int extentZoom = span > 0
            ? Math.Clamp((int)Math.Floor(Math.Log2(2.0 * XyzTileGrid.Extent / span)), 0, XyzTileGrid.MaxZoom)
            : DefaultZoomCeiling;
        int zoom = Math.Clamp(minZoom ?? extentZoom - 2, 0, XyzTileGrid.MaxZoom);

        double tile = 2.0 * XyzTileGrid.Extent / (1 << zoom);
        double Down(double v) => (Math.Floor((v + XyzTileGrid.Extent) / tile) - 1) * tile - XyzTileGrid.Extent;
        double Up(double v) => (Math.Ceiling((v + XyzTileGrid.Extent) / tile) + 1) * tile - XyzTileGrid.Extent;
        double Clamp(double v) => Math.Clamp(v, -XyzTileGrid.Extent, XyzTileGrid.Extent);

        var (grownWest, grownSouth) = WebMercator.ToLonLat(Clamp(Down(minX)), Clamp(Down(minY)));
        var (grownEast, grownNorth) = WebMercator.ToLonLat(Clamp(Up(maxX)), Clamp(Up(maxY)));
        return
        [
            grownWest,
            grownSouth <= -WebMercator.MaxLatitude + 1e-9 ? -90 : grownSouth,
            grownEast,
            grownNorth >= WebMercator.MaxLatitude - 1e-9 ? 90 : grownNorth,
        ];
    }

    /// <summary>
    /// The lowest zoom the default zoom range can start at for
    /// <paramref name="processors"/> and <paramref name="box"/>: the zoom of
    /// the coarsest cell minimum display scale any of them declares, as
    /// <see cref="ResolveZoomRange"/> derives it once they're portrayed. Over
    /// every dataset, so it's no higher than the range of those left after
    /// filtering. <see langword="null"/>, for the stand-in zoom, when a
    /// dataset declares no scale (coverages, GML products), since its
    /// portrayal may still set one.
    /// </summary>
    internal static int? LowestDefaultZoom(IReadOnlyList<IDatasetProcessor> processors, double[] box)
    {
        int coarsest = 0;
        foreach (var processor in processors)
        {
            if (processor.Metadata.DisplayScale?.Minimum is not int scale || scale <= 0)
                return null;
            coarsest = Math.Max(coarsest, scale);
        }

        if (coarsest == 0)
            return null;

        var (_, minY) = WebMercator.FromLonLat(0, Math.Clamp(box[1], -WebMercator.MaxLatitude, WebMercator.MaxLatitude));
        var (_, maxY) = WebMercator.FromLonLat(0, Math.Clamp(box[3], -WebMercator.MaxLatitude, WebMercator.MaxLatitude));
        // The latitude ResolveLayout measures scales at: the box's middle in Web Mercator.
        double referenceLatitude = WebMercator.ToLonLat(0, (minY + maxY) / 2.0).Latitude;
        return XyzTileGrid.ZoomForScaleDenominator(coarsest, referenceLatitude);
    }

    /// <summary>
    /// True when <paramref name="processor"/>'s extent is known and lies wholly
    /// outside the WGS-84 box <c>west, south, east, north</c>. A box or an
    /// extent that crosses the antimeridian, or reaches past ±180°, is never
    /// outside, so nothing that might show is left out.
    /// </summary>
    internal static bool IsOutside(IDatasetProcessor processor, double[] box, ICrsTransformFactory crs)
    {
        BoundingBox? extent;
        try
        {
            extent = processor.Metadata.GetGeographicExtent(crs);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }

        if (extent is null)
            return false;

        double west = box[0], south = box[1], east = box[2], north = box[3];
        if (west > east || west < -180 || east > 180
            || extent.WestLongitude > extent.EastLongitude
            || extent.WestLongitude < -180 || extent.EastLongitude > 180)
        {
            return false;
        }

        return extent.EastLongitude < west || extent.WestLongitude > east
            || extent.NorthLatitude < south || extent.SouthLatitude > north;
    }

    /// <summary>
    /// Portrays the datasets in <paramref name="palette"/> (or the
    /// <c>--palette</c> option) and prepares the composite each zoom level is
    /// rendered from.
    /// </summary>
    /// <param name="palette">The palette; <see langword="null"/> for <c>--palette</c>.</param>
    /// <param name="time">
    /// One of <see cref="Times"/>, each time-varying dataset being portrayed at
    /// its own step nearest it; <see langword="null"/> for <c>--time-step</c>.
    /// </param>
    /// <param name="steps">
    /// With <c>--strict-time</c>, the step each dataset draws, by input index
    /// (<see cref="TimeChoice.Steps"/>): a time-varying dataset without one
    /// is left out. <see langword="null"/> for each dataset's step nearest
    /// <paramref name="time"/>.
    /// </param>
    /// <param name="mariner">
    /// Mariner settings over the start-up ones (issue #1493); <see langword="null"/>
    /// for the start-up ones alone.
    /// </param>
    public TileScene Prepare(string? palette = null, DateTime? time = null, IReadOnlyList<DateTime?>? steps = null, MarinerStyle? mariner = null)
    {
        lock (_portrayGate)
            return PrepareLocked(palette, time, steps, mariner);
    }

    /// <summary>
    /// What draws at <paramref name="instant"/>. By default each time-varying
    /// dataset draws its step nearest the entry of <see cref="Times"/>
    /// nearest the instant, however far away. With <paramref name="strict"/>
    /// (<c>--strict-time</c>), each draws its own step nearest the instant only
    /// when that step is at most half a step away (<see cref="StrictStep"/>),
    /// and nothing otherwise.
    /// </summary>
    public TimeChoice ChooseTime(DateTime instant, bool strict)
    {
        var snapped = SnapTime(instant);
        if (!strict || snapped is not { } at)
            return new TimeChoice(snapped);

        instant = AsUtc(instant);
        var steps = new DateTime?[_processors.Count];
        bool differs = false, drawsSomething = false;
        for (int i = 0; i < _processors.Count; i++)
        {
            if (!VariesWithTime(_processors[i]))
            {
                drawsSomething = true;
                continue;
            }

            var times = ((ITimeAwareDatasetProcessor)_processors[i]).AvailableTimes
                .Select(AsUtc).Distinct().Order().ToArray();
            steps[i] = StrictStep(times, instant);
            drawsSomething |= steps[i] is not null;
            differs |= steps[i] != AsUtc(RenderContextBuilder.Nearest(times, at));
        }

        // When every dataset draws what it would by default, the default
        // scene and its cached tiles serve.
        if (!differs)
            return new TimeChoice(at);

        // A short, stable name for the steps, for cache keys and file names.
        var signature = string.Join('.', steps.Select(s => s is { } t ? t.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-"));
        var variant = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)), 0, 8);
        return new TimeChoice(at, steps, variant, !drawsSomething);
    }

    /// <summary>
    /// The step of <paramref name="sorted"/> nearest <paramref name="instant"/>,
    /// or <see langword="null"/> when it is more than half a step away: before
    /// the first step, after the last or in a gap. A step is the median
    /// interval between steps, an hour for a single step.
    /// </summary>
    /// <param name="sorted">A dataset's steps, ascending and distinct.</param>
    /// <param name="instant">The time asked for.</param>
    internal static DateTime? StrictStep(IReadOnlyList<DateTime> sorted, DateTime instant)
    {
        if (sorted.Count == 0)
            return null;

        var nearest = RenderContextBuilder.Nearest(sorted, instant);
        return (nearest - instant).Duration() <= TypicalStep(sorted) / 2 ? nearest : null;
    }

    /// <summary>The median interval between steps, robust to long gaps; an hour for a single step.</summary>
    private static TimeSpan TypicalStep(IReadOnlyList<DateTime> sorted)
    {
        if (sorted.Count < 2)
            return TimeSpan.FromHours(1);

        var intervals = new TimeSpan[sorted.Count - 1];
        for (int i = 1; i < sorted.Count; i++)
            intervals[i - 1] = sorted[i] - sorted[i - 1];
        Array.Sort(intervals);
        return intervals[intervals.Length / 2];
    }

    /// <summary>
    /// Each time-varying dataset and the step it draws in <paramref name="choice"/>,
    /// <see langword="null"/> when it has no data then.
    /// </summary>
    public IEnumerable<(string Name, string Spec, DateTime? Step)> DescribeTime(TimeChoice choice)
    {
        for (int i = 0; i < _processors.Count; i++)
        {
            if (!VariesWithTime(_processors[i]))
                continue;

            var step = choice.Steps is { } steps
                ? steps[i]
                : choice.Time is { } at ? RenderContextBuilder.NearestTimeStep(_processors[i], at) is { } s ? AsUtc(s) : null : null;
            yield return (Names[i], Specs[i], step);
        }
    }

    /// <summary>
    /// Every time step of the time-varying datasets (S-104, S-111, …), in UTC,
    /// ascending and without duplicates; empty when none of them varies.
    /// </summary>
    public IReadOnlyList<DateTime> Times { get; }

    /// <summary>
    /// The entry of <see cref="Times"/> nearest <paramref name="instant"/>, or
    /// <see langword="null"/> when no dataset varies with time.
    /// </summary>
    public DateTime? SnapTime(DateTime instant) =>
        Times.Count == 0 ? null : RenderContextBuilder.Nearest(Times, AsUtc(instant));

    /// <summary>
    /// <paramref name="time"/> as UTC. Dataset time steps are UTC; one read
    /// without a kind is taken as UTC, not converted from local time.
    /// </summary>
    internal static DateTime AsUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => time,
    };

    private static bool VariesWithTime(IDatasetProcessor processor) =>
        processor is ITimeAwareDatasetProcessor timeAware && timeAware.AvailableTimes.Count > 0;

    private TileScene PrepareLocked(string? palette, DateTime? time, IReadOnlyList<DateTime?>? steps, MarinerStyle? marinerStyle)
    {
        RenderCommand.TryParsePalette(palette ?? _settings.Palette, out var parsedPalette);
        RenderCommand.TryParseBasemap(_settings.Basemap, out var basemap);
        RenderCommand.TryParseDisplayMode(_settings.DisplayMode, out var displayModeId);
        var hidden = HiddenCategories(_settings);
        var nationalLanguage = RenderCommand.ResolveNationalLanguage(_settings.NationalLanguage);
        var effectiveMariner = marinerStyle is null ? _settings.MarinerStyle : marinerStyle.Over(_settings.MarinerStyle);
        string marinerKey = effectiveMariner.Key;
        var mariner = effectiveMariner.ToMarinerSettings(nationalLanguage);
        // Only when chosen, so the processors keep their own defaults (today's tiles) otherwise.
        var contextMariner = effectiveMariner.IsEmpty ? null : mariner;
        var ecdisDisplay = effectiveMariner.ToEcdisDisplay();

        RenderContext ContextFor(IDatasetProcessor processor, Viewport? viewport)
        {
            var context = RenderContextBuilder.Build(
                processor, parsedPalette, _settings.SymbolScale, _settings.TextScale, _settings.TimeStep, hidden,
                displayModeId: displayModeId, viewport: viewport, instant: time,
                nationalLanguage: nationalLanguage, mariner: contextMariner, ecdisDisplay: ecdisDisplay);
            return steps is not null && VariesWithTime(processor)
                ? context with { TimeStep = steps[_processors.IndexOf(processor)] }
                : context;
        }

        // With --strict-time, a time-varying dataset without a step at the
        // time asked for is left out.
        bool Drawn(int index) => steps is null || !VariesWithTime(_processors[index]) || steps[index] is not null;

        // Vector products portray once, independent of the viewport; coverage
        // products portray here without a viewport (the full grid) to find the
        // extent, and again for each zoom level (TileScene.RendererFor). The
        // datasets still to portray run in parallel on the work scheduler.
        var inputs = new ChartCompositeInput[_processors.Count];
        var pending = new List<(int Index, ChartPortrayalRequest Request)>();
        bool hasCoverage = false;
        for (int i = 0; i < _processors.Count; i++)
        {
            if (!Drawn(i))
                continue;

            var processor = _processors[i];
            if (processor is IVectorPortrayalSource
                && !VariesWithTime(processor)
                && _timelessPortrayals.TryGetValue((parsedPalette, marinerKey, i), out var cached))
            {
                inputs[i] = ChartCompositeInput.ForVector(cached);
                continue;
            }

            hasCoverage |= processor is not IVectorPortrayalSource;
            pending.Add((i, new ChartPortrayalRequest(processor, ContextFor(processor, null))));
        }

        var portrayed = ChartRenderer.PortrayAsync([.. pending.Select(p => p.Request)]).GetAwaiter().GetResult();
        for (int p = 0; p < pending.Count; p++)
        {
            int i = pending[p].Index;
            inputs[i] = portrayed[p];
            if (portrayed[p].Vector is { } vector && !VariesWithTime(_processors[i]))
                _timelessPortrayals[(parsedPalette, marinerKey, i)] = vector;
        }

        // Datasets left out by --strict-time are left out of the scene too.
        var drawn = Enumerable.Range(0, _processors.Count).Where(Drawn).ToArray();
        IReadOnlyList<IDatasetProcessor> sceneProcessors = drawn.Length == _processors.Count ? _processors : [.. drawn.Select(i => _processors[i])];
        if (drawn.Length != _processors.Count)
            inputs = [.. drawn.Select(i => inputs[i])];

        var vectorResults = inputs.Select(input => input.Vector).OfType<VectorPortrayalResult>().ToList();
        var options = new ChartCompositeOptions
        {
            Background = Background(_settings),
            Mariner = mariner,
            HiddenCategories = hidden,
            Basemap = basemap,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
            KeepAreaSymbolsInView = false,
        };

        return new TileScene(sceneProcessors, inputs, vectorResults, hasCoverage, ChartCompositorFactory.Create(), options, ContextFor, _portrayGate);
    }

    /// <summary>
    /// Resolves the area to tile and its zoom range from a prepared scene, or
    /// <see langword="null"/> when the datasets have no geometry and no
    /// <c>--bbox</c> is given.
    /// </summary>
    /// <param name="scene">The prepared datasets.</param>
    /// <param name="referenceLatitude">
    /// The latitude zoom levels' scales are measured at; <see langword="null"/>
    /// for the middle of the area. <c>tiles serve --manage-datasets</c> keeps
    /// the first one, so adding a dataset doesn't change the others' tiles.
    /// </param>
    public TileLayout? ResolveLayout(TileScene scene, double? referenceLatitude = null)
    {
        if (!TryResolveArea(_settings, scene.Scene, out var area))
            return null;

        referenceLatitude ??= WebMercator.ToLonLat(0, (area.MinY + area.MaxY) / 2.0).Latitude;
        var (minZoom, maxZoom) = ResolveZoomRange(_settings, scene.VectorResults, area, referenceLatitude.Value);
        var tileOptions = new ChartTileOptions
        {
            PixelRatio = _settings.TileSize / XyzTileGrid.TileSize,
            ReferenceLatitude = referenceLatitude.Value,
            Background = Background(_settings),
        };
        return new TileLayout(area, referenceLatitude.Value, minZoom, maxZoom, tileOptions);
    }

    /// <summary>Encodes a rendered tile in the session's format and quality.</summary>
    public byte[] Encode(ChartTile tile)
    {
        var encodeFormat = Format switch
        {
            TileImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            TileImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using var data = tile.Bitmap.Encode(encodeFormat, _settings.Quality)
            ?? throw new NotSupportedException($"SkiaSharp could not encode a tile as {encodeFormat} on this platform.");
        return data.ToArray();
    }

    /// <summary>The display settings baked into the tiles, as tile-set metadata records them.</summary>
    /// <param name="referenceLatitude">The latitude the zoom levels' scales are given at.</param>
    /// <param name="palette">The palette; <see langword="null"/> for <c>--palette</c>.</param>
    /// <param name="time">The time step; <see langword="null"/> for <c>--time-step</c>.</param>
    /// <param name="mariner">Mariner settings over the start-up ones; <see langword="null"/> for those alone.</param>
    public Dictionary<string, string> DescribeSettings(double referenceLatitude, string? palette = null, DateTime? time = null, MarinerStyle? mariner = null)
    {
        var description = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["generator"] = "s100 " + CliVersionInfo.FromAssembly(typeof(TileRenderSession).Assembly).InformationalVersion,
            ["palette"] = (palette ?? _settings.Palette).Trim().ToLowerInvariant(),
            ["symbolScale"] = _settings.SymbolScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["textScale"] = _settings.TextScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timeStep"] = _settings.TimeStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["basemap"] = _settings.Basemap.Trim().ToLowerInvariant(),
            ["scaleLatitude"] = referenceLatitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
        };
        var hidden = HiddenCategories(_settings);
        if (hidden != DrawingInstructionCategory.None)
            description["hidden"] = hidden.ToString();
        if (RenderCommand.ResolveNationalLanguage(_settings.NationalLanguage) is { } nationalLanguage)
            description["nationalLanguage"] = nationalLanguage;
        if (!string.IsNullOrWhiteSpace(_settings.DisplayMode))
            description["displayMode"] = _settings.DisplayMode.Trim().ToLowerInvariant();
        var effectiveMariner = mariner is null ? _settings.MarinerStyle : mariner.Over(_settings.MarinerStyle);
        foreach (var (name, value) in effectiveMariner.Describe())
            description[name] = value;
        if (time is { } at)
            description["time"] = FormatTime(at);
        return description;
    }

    /// <summary>A time step as ISO 8601 UTC, e.g. <c>2026-10-10T06:00:00Z</c>.</summary>
    public static string FormatTime(DateTime time) =>
        AsUtc(time).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A fingerprint of everything that decides what the tiles look like: the
    /// <c>s100</c> version, every input file's path, size and write time
    /// (including sibling update files, and the whole of an exchange set or
    /// folder, so adding or removing a file changes it), and
    /// the options that change pixels. A persisted tile cache is kept per
    /// fingerprint, so tiles are only reused for the same data and settings.
    /// The palette is not included; it is part of each tile's key.
    /// </summary>
    /// <returns>32 lowercase hexadecimal characters.</returns>
    public string Fingerprint() => Fingerprint(_settings, _changes);

    /// <summary>
    /// The <see cref="Fingerprint()"/> of the datasets <paramref name="settings"/>
    /// name, without opening them: only their files' paths, sizes and write
    /// times are read.
    /// </summary>
    /// <param name="settings">The command's settings.</param>
    /// <param name="changes">Datasets added and removed while serving (issue #1494); <see langword="null"/> for none.</param>
    public static string Fingerprint(TilesRenderSettings settings, DatasetChanges? changes = null)
    {
        var text = SettingsText(settings);
        void Line(string name, object? value) =>
            text.Append(name).Append('=').Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

        foreach (var file in InputFiles(settings))
            FileLine(text, file);

        // Only when there are any, so fingerprints without them still match.
        if (changes is { IsEmpty: false })
        {
            foreach (var added in changes.Added)
            {
                foreach (var file in DatasetFiles(added))
                    FileLine(text, file);
            }

            foreach (var removed in changes.Removed)
                Line("removed", removed);
        }

        return Hash(text);
    }

    /// <summary>
    /// A fingerprint of the options that change pixels and the <c>s100</c>
    /// version, without any input file: what every dataset's tiles share
    /// (<see cref="DatasetTileKeys"/>).
    /// </summary>
    internal string SettingsFingerprint() => Hash(SettingsText(_settings));

    /// <summary>A fingerprint of one dataset's files: its own and its sibling files' paths, sizes and write times.</summary>
    internal static string DatasetFingerprint(string path)
    {
        var text = new System.Text.StringBuilder();
        foreach (var file in DatasetFiles(path))
            FileLine(text, file);
        return Hash(text);
    }

    private static string Hash(System.Text.StringBuilder text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash, 0, 16);
    }

    private static void FileLine(System.Text.StringBuilder text, string file)
    {
        var info = new FileInfo(file);
        text.Append("file=").Append($"{info.FullName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}").Append('\n');
    }

    private static System.Text.StringBuilder SettingsText(TilesRenderSettings settings)
    {
        TryParseImage(settings, out var format);
        var text = new System.Text.StringBuilder();
        void Line(string name, object? value) =>
            text.Append(name).Append('=').Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

        Line("generator", CliVersionInfo.FromAssembly(typeof(TileRenderSession).Assembly).InformationalVersion);
        Line("format", format);
        Line("quality", settings.Quality);
        Line("tileSize", settings.TileSize);
        Line("symbolScale", settings.SymbolScale);
        Line("textScale", settings.TextScale);
        Line("timeStep", settings.TimeStep);
        Line("background", settings.Background?.Trim().ToLowerInvariant());
        Line("hidden", HiddenCategories(settings));
        Line("basemap", settings.Basemap.Trim().ToLowerInvariant());
        Line("displayMode", settings.DisplayMode?.Trim().ToLowerInvariant());
        Line("bbox", settings.BoundingBox);
        Line("only", settings.Only);
        Line("noUpdates", settings.NoUpdates);
        Line("metatile", settings.Metatile);
        Line("luaEngine", LuaEngineKinds.GetName(LuaEngineOption.Resolve(settings.LuaEngine)));
        // Only when set, so fingerprints from before the option existed still match.
        if (RenderCommand.ResolveNationalLanguage(settings.NationalLanguage) is { } nationalLanguage)
            Line("nationalLanguage", nationalLanguage);
        if (!settings.MarinerStyle.IsEmpty)
            Line("mariner", settings.MarinerStyle.Key);
        return text;
    }

    /// <summary>
    /// The files the tiles are made from, in input order: each dataset with
    /// its sibling files (S-101 and S-57 update files share its name), or every
    /// file of an exchange set.
    /// </summary>
    private static IEnumerable<string> InputFiles(TilesRenderSettings settings)
    {
        if (settings.FolderSource is { } folder)
            return FolderFiles(Path.GetFullPath(folder), settings.FingerprintExcludedFolder);

        if (settings.IsExchangeSet)
        {
            var source = Path.GetFullPath(settings.ExchangeSetSource!);
            var root = Directory.Exists(source) ? source
                : source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? null
                : Path.GetDirectoryName(source);
            return root is null
                ? [source]
                : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        }

        var files = new List<string>();
        foreach (var path in settings.IsComposite ? settings.Layers : [settings.Input!])
            files.AddRange(DatasetFiles(path));

        return files;
    }

    /// <summary>A dataset file and its sibling files (S-101 and S-57 update files share its name).</summary>
    private static List<string> DatasetFiles(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        List<string> files = [full];
        files.AddRange(Directory.EnumerateFiles(directory, stem + ".*")
            .Where(f => !string.Equals(f, full, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal));
        return files;
    }

    /// <summary>
    /// Every file in a folder of datasets, recursively and in a stable order,
    /// so a file added to it or removed from it changes the fingerprint.
    /// Hidden files and folders are skipped, as the folder's datasets are found,
    /// and so is <paramref name="excluded"/> (a tile cache kept inside it).
    /// </summary>
    private static List<string> FolderFiles(string root, string? excluded)
    {
        var excludedFull = excluded is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excluded));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (excludedFull is not null && string.Equals(directory, excludedFull, StringComparison.Ordinal))
                continue;
            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, "*", options));
                foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
                    pending.Push(child);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder removed while it was read: the next check sees the change.
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Whether <paramref name="path"/> lies in an S-100 exchange set nested in
    /// <paramref name="root"/> (a folder between them holding a <c>CATALOG.XML</c>).
    /// </summary>
    private static bool IsInsideExchangeSet(string path, string root)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(path));
             directory is not null && directory.Length > rootFull.Length;
             directory = Path.GetDirectoryName(directory))
        {
            if (ExchangeSetInput.LooksLikeExchangeSet(directory))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var processor in _processors)
            (processor as IDisposable)?.Dispose();
        _resolution?.Dispose();
        _catalogueManager.Dispose();
    }

    /// <summary>
    /// Resolves the zoom range: explicit options win; otherwise the coarsest
    /// cell minimum display scale sets the lowest zoom and one level past the
    /// finest compilation scale sets the highest, falling back to the extent
    /// (fit in about one tile, then six levels deeper) for data without them.
    /// </summary>
    internal static (int Min, int Max) ResolveZoomRange(
        TilesRenderSettings settings, IReadOnlyList<VectorPortrayalResult> vectorResults, TileArea area, double referenceLatitude)
    {
        double span = Math.Max(area.MaxX - area.MinX, area.MaxY - area.MinY);
        int extentZoom = span > 0
            ? Math.Clamp((int)Math.Floor(Math.Log2(2.0 * XyzTileGrid.Extent / span)), 0, XyzTileGrid.MaxZoom)
            : DefaultZoomCeiling;

        var minimumScales = vectorResults
            .Select(r => r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();
        var compilationScales = vectorResults
            .Select(r => r.CellCompilationScale ?? r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();

        int defaultMin = minimumScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(minimumScales.Max(), referenceLatitude)
            : extentZoom;
        int defaultMax = compilationScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(compilationScales.Min(), referenceLatitude) + 1
            : Math.Min(defaultMin + 6, DefaultZoomCeiling);

        int min = settings.MinZoom ?? Math.Min(defaultMin, settings.MaxZoom ?? defaultMin);
        int max = settings.MaxZoom ?? Math.Max(Math.Min(defaultMax, XyzTileGrid.MaxZoom), min);
        return (min, Math.Max(min, max));
    }

    private static bool TryResolveArea(TilesRenderSettings settings, ChartComposition scene, out TileArea area)
    {
        if (settings.BoundingBox is not null
            && CompositeViewportBuilder.TryParseDoubles(settings.BoundingBox, 4, out var bb))
        {
            var (minX, minY) = WebMercator.FromLonLat(bb[0], Math.Max(bb[1], -WebMercator.MaxLatitude));
            var (maxX, maxY) = WebMercator.FromLonLat(bb[2], Math.Min(bb[3], WebMercator.MaxLatitude));
            area = new TileArea(minX, minY, maxX, maxY);
            return true;
        }

        if (scene.TryGetWorldBounds(out double sMinX, out double sMinY, out double sMaxX, out double sMaxY))
        {
            area = new TileArea(
                sMinX,
                Math.Max(sMinY, -XyzTileGrid.Extent),
                sMaxX,
                Math.Min(sMaxY, XyzTileGrid.Extent));
            return true;
        }

        area = default;
        return false;
    }

    private static DrawingInstructionCategory HiddenCategories(TilesRenderSettings settings)
    {
        var hidden = DrawingInstructionCategory.None;
        if (settings.Hide is not null && RenderCommand.TryParseHideCategories(settings.Hide, out var parsed, out _))
            hidden |= parsed;
        if (settings.NoText)
            hidden |= DrawingInstructionCategory.Text;
        return hidden;
    }

    private static RgbaColor Background(TilesRenderSettings settings) =>
        settings.Background is not null && RenderCommand.TryParseHexColor(settings.Background, out var color)
            ? color
            : RgbaColor.Transparent;

    private static void TryParseImage(TilesRenderSettings settings, out TileImageFormat format) =>
        TilesExportCommand.TryParseImageFormat(settings.Format, out format);
}

/// <summary>
/// The datasets portrayed in one palette, prepared as a composite. Hands out a
/// <see cref="ChartTileRenderer"/> per zoom level, portraying coverage
/// products again for each so grid sampling and arrow density suit it.
/// <see cref="RendererFor"/> may be called concurrently.
/// </summary>
internal sealed class TileScene
{
    /// <summary>The widest coverage portrayal viewport per axis, in pixels.</summary>
    private const int MaxCoverageViewportPixels = 1 << 20;

    private readonly IReadOnlyList<IDatasetProcessor> _processors;
    private readonly ChartCompositeInput[] _inputs;
    private readonly bool _hasCoverage;
    private readonly ChartCompositor _compositor;
    private readonly ChartCompositeOptions _options;
    private readonly Func<IDatasetProcessor, Viewport?, RenderContext> _contextFor;
    private readonly Lock _portrayGate;
    private readonly ConcurrentDictionary<int, Lazy<ChartTileRenderer>> _renderers = new();

    internal TileScene(
        IReadOnlyList<IDatasetProcessor> processors,
        ChartCompositeInput[] inputs,
        IReadOnlyList<VectorPortrayalResult> vectorResults,
        bool hasCoverage,
        ChartCompositor compositor,
        ChartCompositeOptions options,
        Func<IDatasetProcessor, Viewport?, RenderContext> contextFor,
        Lock portrayGate)
    {
        _processors = processors;
        _inputs = inputs;
        VectorResults = vectorResults;
        _hasCoverage = hasCoverage;
        _compositor = compositor;
        _options = options;
        _contextFor = contextFor;
        _portrayGate = portrayGate;
        Scene = compositor.PrepareAsync(inputs, options).GetAwaiter().GetResult();
    }

    /// <summary>The composite of the datasets' full-extent portrayal.</summary>
    public ChartComposition Scene { get; }

    /// <summary>The vector datasets' portrayal results (for their display scales).</summary>
    public IReadOnlyList<VectorPortrayalResult> VectorResults { get; }

    /// <summary>
    /// The renderer for <paramref name="zoom"/>, created once per zoom level.
    /// Coverage portrayal for the level runs on the work scheduler, so call
    /// this from outside a scheduler job.
    /// </summary>
    public ChartTileRenderer RendererFor(int zoom, TileLayout layout) =>
        _renderers.GetOrAdd(zoom, z => new Lazy<ChartTileRenderer>(() => new ChartTileRenderer(
            _hasCoverage ? _compositor.PrepareAsync(PortrayCoverageForZoom(layout, z), _options).GetAwaiter().GetResult() : Scene,
            layout.TileOptions))).Value;

    /// <summary>
    /// Re-portrays each coverage input for one zoom level, against a viewport
    /// covering the whole tiled area at that zoom's resolution, so grid sampling
    /// and arrow density match the tiles. Vector inputs are reused.
    /// </summary>
    private ChartCompositeInput[] PortrayCoverageForZoom(TileLayout layout, int zoom)
    {
        var area = layout.Area;
        double resolution = XyzTileGrid.Resolution(zoom);
        var (minLon, minLat) = WebMercator.ToLonLat(area.MinX, area.MinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(area.MaxX, area.MaxY, clampLatitude: false);
        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = (int)Math.Clamp(Math.Ceiling((area.MaxX - area.MinX) / resolution), 1, MaxCoverageViewportPixels),
            HeightPixels = (int)Math.Clamp(Math.Ceiling((area.MaxY - area.MinY) / resolution), 1, MaxCoverageViewportPixels),
            ScaleDenominator = XyzTileGrid.ScaleDenominator(zoom, layout.ReferenceLatitude),
        };

        var zoomInputs = _inputs.ToArray();
        lock (_portrayGate)
        {
            var coverage = Enumerable.Range(0, _processors.Count)
                .Where(i => _processors[i] is ICoveragePortrayalSource)
                .ToArray();
            var portrayed = ChartRenderer.PortrayAsync(
                    [.. coverage.Select(i => new ChartPortrayalRequest(_processors[i], _contextFor(_processors[i], viewport)))])
                .GetAwaiter().GetResult();
            for (int c = 0; c < coverage.Length; c++)
                zoomInputs[coverage[c]] = portrayed[c];
        }

        return zoomInputs;
    }
}

/// <summary>
/// What draws at a time asked for (<see cref="TileRenderSession.ChooseTime"/>).
/// </summary>
/// <param name="Time">The entry of <see cref="TileRenderSession.Times"/> nearest the time asked for; <see langword="null"/> for <c>--time-step</c>.</param>
/// <param name="Steps">
/// With <c>--strict-time</c>, when some dataset draws other than at its step
/// nearest <paramref name="Time"/>: the step each dataset draws, by input
/// index, <see langword="null"/> for one that draws nothing (or doesn't vary
/// with time). Otherwise <see langword="null"/>.
/// </param>
/// <param name="Variant">Tells apart choices with the same <paramref name="Time"/> and different <paramref name="Steps"/>; <see langword="null"/> without them.</param>
/// <param name="Empty">Whether nothing draws at all.</param>
internal sealed record TimeChoice(DateTime? Time, IReadOnlyList<DateTime?>? Steps = null, string? Variant = null, bool Empty = false);
