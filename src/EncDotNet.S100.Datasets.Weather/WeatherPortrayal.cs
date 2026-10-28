using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Builds the renderer-neutral portrayal of a <see cref="WeatherCoverageSource"/>:
/// one coverage sub-layer per <see cref="WeatherCoverageStyle"/>, carrying the
/// style's S-98 plane, priority and opacity, ready for the Mapsui renderer or
/// the headless compositor.
/// </summary>
/// <remarks>
/// <para>
/// Each style runs through the ordinary <see cref="CoveragePipeline"/>. A fill
/// becomes a <see cref="GridCoverageSubLayer"/> at the style's
/// <see cref="WeatherCoverageStyle.Opacity"/>, flagged
/// <see cref="GridCoverageSubLayer.ClipToWater"/> when the style is (wave
/// height) and carrying the band legend (<see cref="GridCoverageSubLayer.Legend"/>);
/// symbols become an <see cref="ArrowCoverageSubLayer"/> served from
/// <see cref="WeatherCoverageStyle.Symbols"/>, with southern-hemisphere barbs
/// south of the equator; isolines (pressure) are traced from the style's
/// <see cref="WeatherCoverageStyle.Contours"/> through the sampled grid
/// (<see cref="CoverageContours"/>) into an <see cref="IsolineCoverageSubLayer"/>.
/// Styles whose fields the source lacks
/// (<see cref="WeatherCoverageStyle.AppliesTo"/>) are skipped.
/// </para>
/// <para>
/// This is the part of a weather dataset processor that doesn't depend on a
/// file format; a processor adds the overview level choice and the product
/// registration. It selects the context's time step on the source, so
/// serialise calls that share a source.
/// </para>
/// </remarks>
public static class WeatherPortrayal
{
    /// <summary>
    /// Builds the sub-layers of <paramref name="styles"/> over
    /// <paramref name="source"/>, in the order given.
    /// </summary>
    /// <param name="source">The coverage source.</param>
    /// <param name="styles">The styles, e.g. <see cref="WeatherCoverageStyle.CreateFor"/>.</param>
    /// <param name="sourceDatasetId">The dataset's stable id, typically its file name.</param>
    /// <param name="context">
    /// The render context: its <see cref="RenderContext.TimeStep"/> (the
    /// nearest step; the source's current step when unset),
    /// <see cref="RenderContext.Palette"/>, <see cref="RenderContext.Viewport"/>
    /// (to sample only what is in view), <see cref="RenderContext.SymbolScale"/>
    /// and <see cref="RenderContext.Mariner"/>. <see langword="null"/> draws the
    /// whole grid by Day.
    /// </param>
    /// <param name="datasetContentHash">
    /// A hash of the dataset's content, or <see langword="null"/>. When given,
    /// <see cref="CoveragePortrayalResult.ContentScope"/> is
    /// <c>ds:{hash}|{<see cref="WeatherCoverageStyle.ContentScope"/>}</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The portrayal; its sub-layers may be empty when no style applies.</returns>
    public static async Task<CoveragePortrayalResult> BuildAsync(
        WeatherCoverageSource source,
        IReadOnlyList<WeatherCoverageStyle> styles,
        string sourceDatasetId,
        RenderContext? context = null,
        string? datasetContentHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatasetId);

        if (context?.TimeStep is { } time)
            source.SelectTime(time);

        var subLayers = new List<CoverageSubLayerBase>(styles.Count);
        foreach (var style in styles)
        {
            ArgumentNullException.ThrowIfNull(style, nameof(styles));
            if (!style.AppliesTo(source))
                continue;

            subLayers.Add(await BuildSubLayerAsync(source, style, sourceDatasetId, context, cancellationToken)
                .ConfigureAwait(false));
        }

        var grid = source.Metadata.GridMetadata;
        string info = string.Create(
            CultureInfo.InvariantCulture,
            $"{source.GridSet.Origin.Model} {source.Name} — {grid.NumColumns}×{grid.NumRows} grid, valid {source.SelectedTime:u} ({source.AvailableTimes.Count} steps). Not for navigation.");

        var bounds = GridBounds(source);
        return new CoveragePortrayalResult
        {
            SubLayers = subLayers,
            Spec = source.Spec,
            SourceDatasetId = sourceDatasetId,
            Info = info,
            ContentScope = datasetContentHash is null ? null : $"ds:{datasetContentHash}|{WeatherCoverageStyle.ContentScope}",
            LayerNames = subLayers.Select(l => l.LayerKey).ToArray(),
            CoverageExtent = bounds,
        };
    }

    /// <summary>
    /// Builds the one sub-layer of <paramref name="style"/> over
    /// <paramref name="source"/> at the source's selected time step.
    /// </summary>
    /// <param name="source">The coverage source.</param>
    /// <param name="style">The style; it must apply to the source (<see cref="WeatherCoverageStyle.AppliesTo"/>).</param>
    /// <param name="sourceDatasetId">The dataset's stable id, used in the layer name.</param>
    /// <param name="context">The render context; see <see cref="BuildAsync"/>. Its time step isn't applied here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="GridCoverageSubLayer"/> for a fill, an
    /// <see cref="ArrowCoverageSubLayer"/> for symbols, an
    /// <see cref="IsolineCoverageSubLayer"/> for isolines.
    /// </returns>
    /// <exception cref="ArgumentException">The source lacks a field the style reads.</exception>
    public static async Task<CoverageSubLayerBase> BuildSubLayerAsync(
        WeatherCoverageSource source,
        WeatherCoverageStyle style,
        string sourceDatasetId,
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatasetId);
        if (!style.AppliesTo(source))
            throw new ArgumentException($"The '{source.Name}' source has no field for the {style.Name} style.", nameof(style));

        await style.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        // The grid is geographic (EPSG:4326), so the viewport needs no
        // transform into the native CRS.
        var layer = await new CoveragePipeline().ProcessAsync(
            source,
            style,
            viewport: context?.Viewport,
            wgs84ToNative: null,
            mariner: context?.Mariner ?? MarinerSettings.Default,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var bounds = GridBounds(source);
        var viewport = context?.Viewport ?? FullGridViewport(source, bounds);
        string layerName = $"{style.Name}: {sourceDatasetId}";

        if (style.Kind == WeatherLayerKind.Fill)
        {
            return new GridCoverageSubLayer
            {
                LayerKey = style.LayerKey,
                LayerName = layerName,
                Plane = style.Plane,
                WithinPlanePriority = style.WithinPlanePriority,
                SourceFeatureType = style.LayerKey,
                Coverage = layer,
                Viewport = viewport,
                Opacity = style.Opacity,
                ClipToWater = style.ClipToWater,
                Legend = layer.ColorScheme is { } scheme ? CoverageLegendEntry.FromScheme(scheme) : null,
            };
        }

        if (style.Kind == WeatherLayerKind.Isolines)
        {
            var palette = style.ActivePalette;
            var contours = style.Contours;
            var isolines = CoverageContours.Trace(layer, style.ValueFieldName, contours, style.FormatContourLabel);
            // Labels follow the mariner's text scale, as S-101 labels do.
            double textScale = context?.TextScale ?? 1.0;
            var first = contours.Count > 0 ? contours[0] : null;
            return new IsolineCoverageSubLayer
            {
                LayerKey = style.LayerKey,
                LayerName = layerName,
                Plane = style.Plane,
                WithinPlanePriority = style.WithinPlanePriority,
                SourceFeatureType = style.LayerKey,
                Isolines = isolines,
                Extent = MercatorExtent(bounds),
                Ink = RgbaColor.FromHex(first?.Color ?? palette.Resolve(WeatherPalette.IsobarInkToken)),
                LabelHalo = RgbaColor.FromHex(palette.Resolve(WeatherPalette.IsobarHaloToken)),
                LineWidth = first?.LineWidth ?? 1.2,
                DashPattern = first?.DashPattern is { Length: >= 2 } dash ? dash.Select(d => (double)d).ToArray() : [],
                LabelFontSize = 10 * textScale,
                LabelHaloWidth = 3 * textScale,
            };
        }

        return new ArrowCoverageSubLayer
        {
            LayerKey = style.LayerKey,
            LayerName = layerName,
            Plane = style.Plane,
            WithinPlanePriority = style.WithinPlanePriority,
            SourceFeatureType = style.LayerKey,
            Coverage = layer,
            Viewport = viewport,
            Palette = style.ActivePalette,
            BaseSymbolScale = context?.SymbolScale ?? 1.0,
            SymbolProvider = WeatherCoverageStyle.Symbols.Provider,
            FallbackExtent = bounds,
        };
    }

    // The WGS-84 extent of the grid's nodes at full resolution.
    private static GeographicBounds GridBounds(WeatherCoverageSource source)
    {
        var grid = source.GridSet.Grid;
        double east = grid.OriginLongitude + (grid.Columns - 1) * grid.SpacingLongitudinal;
        double north = grid.OriginLatitude + (grid.Rows - 1) * grid.SpacingLatitudinal;
        return new GeographicBounds(
            Math.Min(grid.OriginLongitude, east),
            Math.Min(grid.OriginLatitude, north),
            Math.Max(grid.OriginLongitude, east),
            Math.Max(grid.OriginLatitude, north));
    }

    // The EPSG:3857 extent of the grid's nodes.
    private static MercatorBounds MercatorExtent(GeographicBounds bounds)
    {
        static (double X, double Y) ToMercator(double longitude, double latitude)
        {
            const double earthRadius = 6378137.0;
            double lat = Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 180.0;
            return (earthRadius * longitude * Math.PI / 180.0, earthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + lat / 2.0)));
        }

        var (minX, minY) = ToMercator(bounds.MinLongitude, bounds.MinLatitude);
        var (maxX, maxY) = ToMercator(bounds.MaxLongitude, bounds.MaxLatitude);
        return new MercatorBounds(minX, minY, maxX, maxY);
    }

    private static Viewport FullGridViewport(WeatherCoverageSource source, GeographicBounds bounds)
    {
        var grid = source.Metadata.GridMetadata;
        return new Viewport
        {
            MinLatitude = bounds.MinLatitude,
            MaxLatitude = bounds.MaxLatitude,
            MinLongitude = bounds.MinLongitude,
            MaxLongitude = bounds.MaxLongitude,
            WidthPixels = Math.Max(1, grid.NumColumns),
            HeightPixels = Math.Max(1, grid.NumRows),
            ScaleDenominator = 50_000,
        };
    }
}
