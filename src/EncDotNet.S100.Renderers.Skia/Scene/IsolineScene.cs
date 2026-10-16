using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// One labelled isoline: a polyline in Web Mercator (EPSG:3857) metres.
/// </summary>
/// <param name="Label">The text placed along the line, e.g. <c>"0.5"</c>.</param>
/// <param name="MercatorCoordinates">Interleaved <c>x0, y0, x1, y1, …</c> vertices.</param>
public sealed record IsolinePolyline(string Label, double[] MercatorCoordinates);

/// <summary>
/// The look of an <see cref="IsolineScene"/>: one ink colour, a dashed line and
/// haloed monospace labels. Sizes are device-independent pixels.
/// </summary>
public sealed class IsolineAppearance
{
    /// <summary>Line and label colour.</summary>
    public required SKColor Ink { get; init; }

    /// <summary>Label halo colour.</summary>
    public required SKColor LabelHalo { get; init; }

    /// <summary>Line width.</summary>
    public float LineWidth { get; init; } = 1.1f;

    /// <summary>Dash pattern (dash, gap, …); empty for a solid line.</summary>
    public IReadOnlyList<float> DashPattern { get; init; } = [6f, 3f];

    /// <summary>Label font size.</summary>
    public float LabelFontSize { get; init; } = 10f;

    /// <summary>The line length between labels.</summary>
    public float LabelSpacing { get; init; } = 250f;
}

/// <summary>
/// Prepared isolines for repeated drawing in screen space: the viewer draws a
/// scene every frame through a Mapsui style renderer, and the headless
/// composite draws it once. Lines are dashed and labelled in screen pixels, so
/// the dash and label spacing stay constant at every zoom. When land areas are
/// supplied the lines are clipped to water (issue #483; R-101-104-B).
/// </summary>
/// <remarks>
/// Geometry is held relative to an origin inside the scene so single-precision
/// path coordinates keep sub-metre precision; each draw maps it to the screen
/// with one affine matrix.
/// </remarks>
public sealed class IsolineScene : IDisposable
{
    private const double ProbeMetres = 1000.0;
    private const float MinimumLabelledLength = 40f;

    private readonly double _originX;
    private readonly double _originY;
    private readonly (string Label, SKPath Path)[] _lines;
    private readonly SKPath? _land;

    /// <summary>
    /// Builds a scene from <paramref name="lines"/>, clipped against
    /// <paramref name="landAreas"/> when given.
    /// </summary>
    /// <param name="lines">The isolines.</param>
    /// <param name="appearance">How to draw them.</param>
    /// <param name="landAreas">
    /// Land-area surfaces in WGS84 to clip the lines out of, or
    /// <see langword="null"/> to draw them everywhere.
    /// </param>
    public IsolineScene(
        IReadOnlyList<IsolinePolyline> lines,
        IsolineAppearance appearance,
        IReadOnlyList<FeatureGeometry>? landAreas = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(appearance);
        Appearance = appearance;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var line in lines)
        {
            var xy = line.MercatorCoordinates;
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                minX = Math.Min(minX, xy[i]);
                maxX = Math.Max(maxX, xy[i]);
                minY = Math.Min(minY, xy[i + 1]);
                maxY = Math.Max(maxY, xy[i + 1]);
            }
        }

        bool empty = !double.IsFinite(minX);
        _originX = empty ? 0 : (minX + maxX) / 2.0;
        _originY = empty ? 0 : (minY + maxY) / 2.0;

        _lines = new (string, SKPath)[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            var xy = lines[i].MercatorCoordinates;
            var path = new SKPath();
            for (int v = 0; v + 1 < xy.Length; v += 2)
            {
                var point = ToLocal(xy[v], xy[v + 1]);
                if (v == 0)
                    path.MoveTo(point);
                else
                    path.LineTo(point);
            }
            _lines[i] = (lines[i].Label, path);
        }

        if (!empty && landAreas is { Count: > 0 })
        {
            _land = CoverageLandClip.BuildLandPath(landAreas, (lon, lat) =>
            {
                var (x, y) = WebMercator.FromLonLat(lon, lat);
                return ToLocal(x, y);
            });
        }
    }

    /// <summary>How the scene is drawn.</summary>
    public IsolineAppearance Appearance { get; }

    /// <summary>Whether the scene has no lines.</summary>
    public bool IsEmpty => _lines.Length == 0;

    /// <summary>
    /// Draws the lines (and, unless <paramref name="drawLabels"/> is false, the
    /// labels) onto <paramref name="canvas"/>.
    /// </summary>
    /// <param name="canvas">The canvas, in screen pixels.</param>
    /// <param name="mercatorToScreen">Projects Web Mercator metres to screen pixels.</param>
    /// <param name="opacity">Overall opacity, 0 to 1.</param>
    /// <param name="drawLabels">Whether to draw the labels with the lines.</param>
    public void Draw(SKCanvas canvas, Func<double, double, SKPoint> mercatorToScreen, float opacity = 1f, bool drawLabels = true)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(mercatorToScreen);
        if (IsEmpty || opacity <= 0)
            return;

        var matrix = LocalToScreen(mercatorToScreen);
        int save = canvas.Save();
        try
        {
            ClipToWater(canvas, matrix);

            using var linePaint = new SKPaint
            {
                Color = WithOpacity(Appearance.Ink, opacity),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Appearance.LineWidth,
                StrokeCap = SKStrokeCap.Butt,
                StrokeJoin = SKStrokeJoin.Round,
            };
            using var dash = Appearance.DashPattern.Count >= 2
                ? SKPathEffect.CreateDash(Appearance.DashPattern.ToArray(), 0)
                : null;
            linePaint.PathEffect = dash;

            foreach (var (_, path) in _lines)
            {
                using var screen = new SKPath();
                path.Transform(matrix, screen);
                canvas.DrawPath(screen, linePaint);
            }

            if (drawLabels)
            {
                foreach (var anchor in LabelAnchors(matrix))
                    DrawLabel(canvas, anchor.Label, anchor.Point, opacity);
            }
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>
    /// Draws only the labels, each at its anchor turned by
    /// <paramref name="rotationDegrees"/> about
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>) and kept upright.
    /// Used by the headless composite on a rotated display, which draws the lines
    /// north-up and rotates them as an image.
    /// </summary>
    /// <param name="canvas">The canvas, in screen pixels.</param>
    /// <param name="mercatorToScreen">Projects Web Mercator metres to north-up screen pixels.</param>
    /// <param name="rotationDegrees">Display rotation, clockwise.</param>
    /// <param name="centerX">Rotation centre X.</param>
    /// <param name="centerY">Rotation centre Y.</param>
    /// <param name="cullBounds">Labels whose turned anchor falls outside are skipped.</param>
    public void DrawLabelsRotated(
        SKCanvas canvas,
        Func<double, double, SKPoint> mercatorToScreen,
        double rotationDegrees,
        float centerX,
        float centerY,
        SKRect cullBounds)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(mercatorToScreen);
        if (IsEmpty)
            return;

        var matrix = LocalToScreen(mercatorToScreen);
        var turn = SKMatrix.CreateRotationDegrees((float)rotationDegrees, centerX, centerY);
        foreach (var anchor in LabelAnchors(matrix))
        {
            var point = turn.MapPoint(anchor.Point);
            if (cullBounds.Contains(point))
                DrawLabel(canvas, anchor.Label, point, 1f);
        }
    }

    /// <summary>
    /// The label anchors for the current view: one per
    /// <see cref="IsolineAppearance.LabelSpacing"/> of screen length (at least
    /// one per line long enough to carry it), spread evenly along each line and
    /// skipped where they would sit on land.
    /// </summary>
    internal IEnumerable<(string Label, SKPoint Point)> LabelAnchors(SKMatrix localToScreen)
    {
        bool canInvert = localToScreen.TryInvert(out var screenToLocal);
        foreach (var (label, path) in _lines)
        {
            using var screen = new SKPath();
            path.Transform(localToScreen, screen);
            using var measure = new SKPathMeasure(screen, forceClosed: false);
            do
            {
                float length = measure.Length;
                if (length < MinimumLabelledLength)
                    continue;

                int count = Math.Max(1, (int)(length / Appearance.LabelSpacing));
                for (int i = 0; i < count; i++)
                {
                    if (!measure.GetPosition((i + 0.5f) * length / count, out var point))
                        continue;
                    if (_land is not null && canInvert)
                    {
                        var local = screenToLocal.MapPoint(point);
                        if (_land.Contains(local.X, local.Y))
                            continue;
                    }
                    yield return (label, point);
                }
            }
            while (measure.NextContour());
        }
    }

    /// <summary>
    /// The matrix from the scene's local frame to the screen, found from three
    /// projected points (the Mercator → screen map is affine for a north-up or
    /// rotated display).
    /// </summary>
    internal SKMatrix LocalToScreen(Func<double, double, SKPoint> mercatorToScreen)
    {
        var origin = mercatorToScreen(_originX, _originY);
        var east = mercatorToScreen(_originX + ProbeMetres, _originY);
        // Local Y is flipped (north is up on screen), so local +Y is Mercator south.
        var south = mercatorToScreen(_originX, _originY - ProbeMetres);
        return new SKMatrix
        {
            ScaleX = (float)((east.X - origin.X) / ProbeMetres),
            SkewX = (float)((south.X - origin.X) / ProbeMetres),
            TransX = origin.X,
            SkewY = (float)((east.Y - origin.Y) / ProbeMetres),
            ScaleY = (float)((south.Y - origin.Y) / ProbeMetres),
            TransY = origin.Y,
            Persp2 = 1,
        };
    }

    private void ClipToWater(SKCanvas canvas, SKMatrix matrix)
    {
        if (_land is null)
            return;

        // The clip is recorded in device space, so restoring the matrix keeps it.
        var before = canvas.TotalMatrix;
        canvas.Concat(in matrix);
        canvas.ClipPath(_land, SKClipOperation.Difference, antialias: true);
        canvas.SetMatrix(in before);
    }

    private void DrawLabel(SKCanvas canvas, string label, SKPoint point, float opacity)
    {
        // Centre the label on the line, both ways.
        float baseline = point.Y + Appearance.LabelFontSize * 0.36f;
        WaterLevelGaugePainter.DrawHaloText(
            canvas,
            label,
            point.X,
            baseline,
            WaterLevelGaugePainter.LabelTypeface,
            Appearance.LabelFontSize,
            WithOpacity(Appearance.Ink, opacity),
            WithOpacity(Appearance.LabelHalo, opacity),
            SKTextAlign.Center);
    }

    private SKPoint ToLocal(double x, double y) =>
        new((float)(x - _originX), (float)(_originY - y));

    private static SKColor WithOpacity(SKColor color, float opacity) =>
        opacity >= 1f ? color : color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0f, 1f)));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var (_, path) in _lines)
            path.Dispose();
        _land?.Dispose();
    }
}

/// <summary>
/// Paints an <see cref="IsolineScene"/> into a headless composite.
/// </summary>
public sealed class IsolineCompositeLayer : CompositeLayer
{
    private readonly IsolineScene _scene;

    /// <summary>Creates the layer.</summary>
    /// <param name="scene">The isolines to draw. The layer does not own it.</param>
    public IsolineCompositeLayer(IsolineScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);
        if (Project(viewport) is { } project)
            _scene.Draw(canvas, project);
    }

    internal override void DrawRotating(SKCanvas canvas, Viewport viewport)
    {
        if (Project(viewport) is { } project)
            _scene.Draw(canvas, project, drawLabels: false);
    }

    internal override void DrawScreenPlaced(SKCanvas canvas, Viewport viewport, double rotationDegrees, SKRect cullBounds)
    {
        if (Project(viewport) is { } project)
            _scene.DrawLabelsRotated(canvas, project, rotationDegrees, viewport.WidthPixels / 2f, viewport.HeightPixels / 2f, cullBounds);
    }

    private static Func<double, double, SKPoint>? Project(Viewport viewport)
    {
        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0)
            return null;

        double scaleX = viewport.WidthPixels / spanX;
        double scaleY = viewport.HeightPixels / spanY;
        return (x, y) => new SKPoint((float)((x - minX) * scaleX), (float)((maxY - y) * scaleY));
    }
}
