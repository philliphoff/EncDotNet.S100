using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Cap;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for CAP 1.2 marine weather warnings (design note
/// <c>docs/design/weather-sources.md</c> §6.1): one CAP alert, an Atom feed of
/// them, or the NWS GeoJSON form from <c>api.weather.gov/alerts</c>, read by
/// <see cref="CapReader"/>. CAP isn't an S-100 product, and its warnings aren't
/// for navigation.
/// </summary>
/// <remarks>
/// <para>What it provides:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Areas.</b> Each CAP area is one feature (<see cref="FeatureType"/>):
/// its polygons, its circles as 64-vertex polygons, or, for an area with only
/// zone codes, the NWS marine zones it names, from
/// <see cref="MarineZoneLayer.Default"/> (see <see cref="CapWarningSet"/>).
/// Codes the layer doesn't have are listed in the pick report and in
/// <see cref="Warnings"/>, not dropped.
/// </description></item>
/// <item><description>
/// <b>Time.</b> An area is drawn only while it is in force at the render
/// context's <see cref="RenderContext.TimeStep"/>, by the rule S-124
/// navigational warnings use (<see cref="WarningValidity"/>), and every
/// drawable area is drawn when the context has no time step. As an
/// <see cref="ITimeAwareDatasetProcessor"/> its steps are the instants the
/// set in force changes (<see cref="CapWarningSet.Changes"/>), held until the
/// next one (<see cref="TimeStepSelection.AtOrBefore"/>), so a host's timeline
/// redraws it exactly when a warning starts or ends.
/// </description></item>
/// <item><description>
/// <b>Drawing.</b> <see cref="CapStyle"/>, built with the code-defined
/// portrayal toolkit: outlines and hatching by severity and the event as a
/// label, one sub-layer on the <see cref="S98DisplayPlane.CautionsAndWarnings"/>
/// plane at priority <see cref="WithinPlanePriority"/>, above S-124 (0).
/// </description></item>
/// <item><description>
/// <b>Picking.</b> The headline, event, severity, urgency, certainty, times,
/// sender, description and instruction (<see cref="GetFeatureInfoAt"/>),
/// led by <see cref="NotForNavigationNotice"/>. A pick only finds areas the
/// last render drew.
/// </description></item>
/// </list>
/// <para>
/// CAP alerts aren't listed as navigational warnings
/// (<see cref="IDatasetProcessor.GetNavigationalWarnings"/> is empty): they
/// don't follow S-124's numbering or cancellation rules.
/// </para>
/// <para>
/// The file is read whole at construction, which fails with a
/// <see cref="CapFormatException"/> naming the file when it isn't CAP.
/// </para>
/// </remarks>
public sealed class CapDatasetProcessor :
    IDatasetProcessor,
    IVectorPortrayalSource,
    IHeadlessImageRenderer,
    ITimeAwareDatasetProcessor,
    ILoadedDatasetProjection
{
    /// <summary>The spec name CAP files are registered under.</summary>
    public const string SpecName = Core.SpecName.Cap;

    /// <summary>The feature type of every CAP area.</summary>
    public const string FeatureType = "CapWarningArea";

    /// <summary>The first line of every pick report on a CAP warning.</summary>
    public const string NotForNavigationNotice = "Weather warning (CAP), not an S-100 product and not for navigation.";

    /// <summary>The CAP sub-layer's order within the cautions and warnings plane: after S-124's 0.</summary>
    public const int WithinPlanePriority = 10;

    /// <summary>The CAP sub-layer's key.</summary>
    public const string LayerKey = "cap.warnings";

    /// <summary>The spec every CAP file reports: <c>CAP/1.2.0</c>.</summary>
    public static SpecRef Cap12Spec { get; } = SpecRef.Parse("CAP/1.2.0");

    /// <summary>
    /// CAP warnings in time: each step is the start or end of a warning, held
    /// until the next step. Validity is the dataset's longest gap between steps,
    /// so the dataset never hides between them.
    /// </summary>
    private readonly TimeSemantics _timeSemantics;

    private readonly CapWarningSet _warnings;
    private readonly CapAreaFeature[] _features;
    private readonly string _fileName;
    private readonly string? _contentHash;
    private readonly DatasetMetadata _metadata;
    private volatile IReadOnlySet<string> _drawn;

    /// <summary>Reads the CAP file at <paramref name="path"/>.</summary>
    /// <param name="path">The file's path.</param>
    public CapDatasetProcessor(string path)
        : this(File.OpenRead(path), Path.GetFileName(path), null)
    {
    }

    /// <summary>Reads the CAP file <paramref name="relativePath"/> from <paramref name="source"/>.</summary>
    /// <param name="source">The asset source holding the file.</param>
    /// <param name="relativePath">The file's source-relative path.</param>
    public CapDatasetProcessor(IAssetSource source, string relativePath)
        : this(AssetSourceHelpers.OpenSeekable(source, relativePath), AssetSourceHelpers.GetFileName(relativePath), null)
    {
    }

    /// <summary>
    /// Reads CAP from <paramref name="stream"/>, which the processor disposes,
    /// resolving zone codes against <paramref name="zones"/>.
    /// </summary>
    /// <param name="stream">The file's bytes; disposed before the constructor returns.</param>
    /// <param name="fileName">The file's name, the dataset's id.</param>
    /// <param name="zones">The marine zones; <see cref="MarineZoneLayer.Default"/> when <see langword="null"/>.</param>
    public CapDatasetProcessor(Stream stream, string fileName, MarineZoneLayer? zones)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _fileName = fileName;
        try
        {
            _contentHash = DatasetContentHash.HashAndRewind(ref stream);
            _warnings = CapWarningSet.Build(CapReader.Read(stream), zones);
        }
        catch (CapFormatException ex)
        {
            throw new CapFormatException($"{fileName}: {ex.Message}", ex);
        }
        finally
        {
            stream.Dispose();
        }

        _features = _warnings.Areas.Select(a => new CapAreaFeature(a)).ToArray();
        _drawn = _warnings.DrawnAt(null).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        AvailableTimes = _warnings.Changes;
        _timeSemantics = new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.AtOrBefore, LongestGap(_warnings));
        _metadata = BuildMetadata(_warnings);
    }

    /// <inheritdoc />
    public SpecRef Spec => Cap12Spec;

    /// <summary>The dataset's file name.</summary>
    public string FileName => _fileName;

    /// <summary>Every area of the file, resolved and timed.</summary>
    public CapWarningSet Warnings => _warnings;

    /// <inheritdoc />
    /// <remarks>The areas' WGS-84 extent and the first start to the last end of the drawable warnings.</remarks>
    public DatasetMetadata Metadata => _metadata;

    /// <inheritdoc />
    public IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <inheritdoc />
    public TimeSemantics TimeSemantics => _timeSemantics;

    /// <summary>Reads the metadata of the CAP file at <paramref name="path"/>: its spec, extent and time coverage.</summary>
    /// <param name="path">The file's path.</param>
    public static DatasetMetadata ReadMetadata(string path)
    {
        try
        {
            return BuildMetadata(CapWarningSet.Build(CapReader.Read(path)));
        }
        catch (CapFormatException ex)
        {
            throw new CapFormatException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    /// <summary>Builds the dataset metadata of a set of CAP areas.</summary>
    /// <param name="warnings">The areas.</param>
    public static DatasetMetadata BuildMetadata(CapWarningSet warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        foreach (var position in warnings.Areas.SelectMany(a => a.Surfaces).SelectMany(s => s.ExteriorRing))
        {
            south = Math.Min(south, position.Latitude);
            north = Math.Max(north, position.Latitude);
            west = Math.Min(west, position.Longitude);
            east = Math.Max(east, position.Longitude);
        }

        var drawable = warnings.Areas.Where(a => a.IsDrawable).ToArray();
        var starts = drawable.Select(a => a.Validity.Start).OfType<DateTimeOffset>().ToArray();
        var ends = drawable.Select(a => a.SupersededAt is { } s && (a.Validity.End is not { } e || s < e) ? s : a.Validity.End)
            .OfType<DateTimeOffset>().ToArray();
        TimeCoverage? time = starts.Length == 0 && ends.Length == 0
            ? null
            : new TimeCoverage(
                (starts.Length > 0 ? starts.Min() : ends.Min()).UtcDateTime,
                (ends.Length > 0 ? ends.Max() : starts.Max()).UtcDateTime);

        return new DatasetMetadata
        {
            Spec = Cap12Spec,
            Extent = south <= north ? new BoundingBox(south, west, north, east) : null,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = time,
        };
    }

    /// <summary>The areas drawn at <paramref name="timeStep"/>, or every drawable area when it is <see langword="null"/>.</summary>
    /// <param name="timeStep">The render context's time step (UTC), or <see langword="null"/>.</param>
    public IReadOnlyList<CapWarningArea> DrawnAt(DateTime? timeStep) =>
        _warnings.DrawnAt(ToOffset(timeStep)).ToArray();

    /// <inheritdoc />
    public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (drawn, instructions) = Portray(context);
        var palette = CapStyle.Palette.GetColorPalette(context?.Palette ?? PaletteType.Day);
        var info = $"CAP {_fileName}: {drawn.Count} of {_warnings.Areas.Count} warning areas in force. {NotForNavigationNotice}";

        var subLayer = new VectorSubLayer
        {
            LayerKey = LayerKey,
            LayerName = $"CAP warnings: {_fileName}",
            Instructions = instructions,
            Plane = S98DisplayPlane.CautionsAndWarnings,
            WithinPlanePriority = WithinPlanePriority,
        };

        return Task.FromResult(new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = palette,
            GeometryProvider = new FeatureGeometryProvider<CapAreaFeature>(_features),
            Product = SpecName,
            Spec = Spec,
            SourceDatasetId = _fileName,
            ContentScope = _contentHash is null ? null : $"ds:{_contentHash}|{CapStyle.ContentScope}",
            Info = info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = CapStyle.Symbols.Provider,
            AreaFillProvider = CapStyle.ResolveAreaFill,
            LayerNames = [LayerKey],
            GeographicExtent = PaddedExtent(),
            PreferLiveDrawing = true,
        });
    }

    /// <summary>
    /// Renders the warnings in force at the context's time step to a bitmap,
    /// framed on every area of the file (or on <see cref="RenderContext.Viewport"/>).
    /// </summary>
    /// <param name="widthPixels">Output width in pixels.</param>
    /// <param name="heightPixels">Output height in pixels.</param>
    /// <param name="context">Palette, time step, scales and viewport.</param>
    /// <param name="background">Background fill; opaque white by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new bitmap owned by the caller.</returns>
    public Task<SKBitmap> RenderHeadlessAsync(
        int widthPixels,
        int heightPixels,
        RenderContext? context = null,
        RgbaColor? background = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);
        cancellationToken.ThrowIfCancellationRequested();

        var (_, instructions) = Portray(context);
        var viewport = context?.Viewport;
        if (viewport is null && _metadata.Extent is { } extent)
        {
            var (minX, minY) = WebMercator.FromLonLat(extent.WestLongitude, extent.SouthLatitude);
            var (maxX, maxY) = WebMercator.FromLonLat(extent.EastLongitude, extent.NorthLatitude);
            viewport = PointGlyphHeadlessAdapter.FitViewport(
                new MercatorBounds(minX, minY, maxX, maxY), widthPixels, heightPixels, padding: 0.08);
        }

        var bg = background ?? new RgbaColor(255, 255, 255, 255);
        if (viewport is null)
            return Task.FromResult(HeadlessVectorRenderer.RenderBlank(widthPixels, heightPixels, bg));

        return Task.FromResult(HeadlessVectorRenderer.Render(
            instructions,
            new FeatureGeometryProvider<CapAreaFeature>(_features),
            CapStyle.Palette.GetColorPalette(context?.Palette ?? PaletteType.Day),
            symbolProvider: CapStyle.Symbols.Provider,
            lineStyleProvider: null,
            symbolScale: context?.SymbolScale ?? 1.0,
            textScale: context?.TextScale ?? 1.0,
            widthPixels: widthPixels,
            heightPixels: heightPixels,
            background: bg,
            areaFillProvider: CapStyle.ResolveAreaFill,
            hiddenCategories: context?.HiddenInstructionCategories ?? DrawingInstructionCategory.None,
            basemap: context?.Basemap ?? BasemapKind.None,
            viewport: viewport));
    }

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);
        var index = Array.FindIndex(_features, f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : GetFeatureInfoAt(index);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The attributes, in order: the notice, headline, event, severity,
    /// urgency, certainty, onset (or effective), expires, the hazard's end
    /// when the NWS gives one, sent, the sender, the area, its zones and any
    /// zone codes not found, the message type and status, a supersession,
    /// the description, the instruction and the identifier. Missing values
    /// are left out.
    /// </remarks>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _features.Length)
            return null;
        var area = _features[ordinal].Area;
        var info = area.Info;
        var alert = area.Alert;
        var attributes = new List<PickAttribute>
        {
            Text("notice", "Notice", NotForNavigationNotice),
        };

        void Add(string code, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                attributes.Add(Text(code, name, value));
        }

        void AddTime(string code, string name, DateTimeOffset? value)
        {
            if (value is { } time)
            {
                attributes.Add(new PickAttribute
                {
                    Code = code,
                    Name = name,
                    RawValue = time.ToString("o", CultureInfo.InvariantCulture),
                    DisplayValue = time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                    DateTimeValue = time.UtcDateTime,
                });
            }
        }

        Add("headline", "Headline", info.Headline);
        Add("event", "Event", info.Event);
        Add("severity", "Severity", info.Severity.ToString());
        Add("urgency", "Urgency", info.Urgency);
        Add("certainty", "Certainty", info.Certainty);
        AddTime(info.Onset is null ? "effective" : "onset", info.Onset is null ? "Effective" : "Onset", info.Onset ?? info.Effective);
        AddTime("expires", "Expires", info.Expires);
        if (info.Ends is { } ends && ends != info.Expires)
            AddTime("ends", "Hazard ends", ends);
        AddTime("sent", "Sent", alert.Sent == default ? null : alert.Sent);
        Add("sender", "Sender", info.SenderName is { } senderName && alert.Sender.Length > 0
            ? $"{senderName} ({alert.Sender})"
            : info.SenderName ?? alert.Sender);
        Add("areaDesc", "Area", area.Area.Description);
        if (area.ShapeSource == CapShapeSource.Zones || area.Zones.Count > 0)
            Add("zones", "Zones", string.Join(", ", area.Zones.Select(z => z.Code)));
        if (area.UnmatchedZoneCodes.Count > 0)
            Add("unmatchedZones", "Zones not found", string.Join(", ", area.UnmatchedZoneCodes));
        Add("msgType", "Message type", $"{alert.MessageType} ({alert.Status})");
        if (area.SupersededBy is { } by)
            Add("supersededBy", "Superseded by", by);
        Add("description", "Description", info.Description);
        Add("instruction", "Instruction", info.Instruction);
        Add("identifier", "Identifier", alert.Identifier);

        return new FeatureInfo
        {
            FeatureRef = area.Id,
            FeatureType = FeatureType,
            FeatureTypeName = string.IsNullOrWhiteSpace(info.Event) ? "Weather warning" : info.Event,
            Attributes = attributes,
        };
    }

    /// <inheritdoc />
    public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _features.Length || _features[ordinal].ExteriorRing.Count == 0)
            return null;
        var feature = _features[ordinal];
        return new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Surface,
            ExteriorRing = feature.ExteriorRing,
            InteriorRings = feature.InteriorRings,
        };
    }

    /// <inheritdoc />
    /// <remarks>Only the areas the last render drew (every drawable area before the first render).</remarks>
    public IReadOnlyList<FeatureGeometryHit> HitTestFeatures(double latitude, double longitude, double radiusMeters)
    {
        var drawn = _drawn;
        var hits = FeatureHitTester.HitTest(_features, latitude, longitude, radiusMeters);
        return hits.Count == 0 || hits.All(h => drawn.Contains(h.FeatureRef))
            ? hits
            : [.. hits.Where(h => drawn.Contains(h.FeatureRef))];
    }

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (var i = 0; i < _features.Length; i++)
        {
            yield return new FeatureSummary
            {
                FeatureRef = _features[i].Id,
                Ordinal = i,
                FeatureType = FeatureType,
                FeatureTypeName = _features[i].Area.Info.Event,
            };
        }
    }

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new CapDatasetData(_warnings, _metadata);

    private (IReadOnlyList<CapWarningArea> Drawn, IReadOnlyList<DrawingInstruction> Instructions) Portray(RenderContext? context)
    {
        var drawn = DrawnAt(context?.TimeStep);
        _drawn = drawn.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        return (drawn, CapStyle.BuildInstructions(drawn));
    }

    private GeographicBounds PaddedExtent()
    {
        if (_metadata.Extent is not { } e)
            return new GeographicBounds(0, 0, 0, 0);
        var latPad = Math.Max(0.01, (e.NorthLatitude - e.SouthLatitude) * 0.1);
        var lonPad = Math.Max(0.01, (e.EastLongitude - e.WestLongitude) * 0.1);
        return new GeographicBounds(e.WestLongitude - lonPad, e.SouthLatitude - latPad, e.EastLongitude + lonPad, e.NorthLatitude + latPad);
    }

    private static PickAttribute Text(string code, string name, string value) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = value };

    private static DateTimeOffset? ToOffset(DateTime? time) =>
        time is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;

    // The longest gap between steps, so a host holding each step until the
    // next never hides the dataset between them; a day when there is one step
    // or none, and ten years when a warning never ends.
    private static TimeSpan LongestGap(CapWarningSet warnings)
    {
        if (warnings.Areas.Any(a => a.IsDrawable && a.Validity.End is null && a.SupersededAt is null))
            return TimeSpan.FromDays(3650);
        var times = warnings.Changes;
        var gap = TimeSpan.FromDays(1);
        if (times.Count > 1)
        {
            gap = TimeSpan.FromMinutes(1);
            for (var i = 1; i < times.Count; i++)
            {
                var step = times[i] - times[i - 1];
                if (step > gap)
                    gap = step;
            }
        }

        return gap;
    }
}

/// <summary>A CAP area as a feature: its outline, for drawing, picking and hit tests.</summary>
internal sealed class CapAreaFeature : IS100Feature
{
    private static readonly IReadOnlyDictionary<string, string> NoAttributes = new Dictionary<string, string>();

    public CapAreaFeature(CapWarningArea area)
    {
        Area = area;
        Surfaces = area.Surfaces;
        ExteriorRing = area.Surfaces.SelectMany(s => s.ExteriorRing).ToArray();
        InteriorRings = area.Surfaces.SelectMany(s => s.InteriorRings).ToArray();
    }

    public CapWarningArea Area { get; }

    public string Id => Area.Id;

    public string FeatureType => CapDatasetProcessor.FeatureType;

    public S100GeometryType GeometryType => S100GeometryType.Surface;

    public IReadOnlyList<GeoPosition> Points => [];

    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves => [];

    public IReadOnlyList<GeoPosition> ExteriorRing { get; }

    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; }

    public IReadOnlyList<SurfacePart> Surfaces { get; }

    public IReadOnlyDictionary<string, string> Attributes => NoAttributes;

    public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes => [];
}
