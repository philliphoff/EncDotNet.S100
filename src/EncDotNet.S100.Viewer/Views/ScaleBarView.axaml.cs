using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Map scale bar overlay. Renders 2-4 segments showing whole-number (or
/// halved/thirded/quartered) nautical-mile lengths sized to the current
/// viewport. Filled and outlined segments alternate in the accent color.
/// </summary>
public partial class ScaleBarView : UserControl
{
    private const double TargetPixelWidth = 200.0;
    private const double EarthRadiusMeters = 6378137.0;
    private const double MetersPerNauticalMile = 1852.0;

    public ScaleBarView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Recomputes the scale bar for the current EPSG:3857 viewport.
    /// </summary>
    /// <param name="mercatorResolution">Map resolution in mercator meters per pixel.</param>
    /// <param name="mercatorCenterY">Mercator Y coordinate of the viewport center (used to correct for latitude distortion).</param>
    public void UpdateForViewport(double mercatorResolution, double mercatorCenterY)
    {
        if (double.IsNaN(mercatorResolution) || mercatorResolution <= 0)
        {
            ClearBar();
            return;
        }

        // Convert mercator y to latitude to remove web-mercator scale distortion.
        var latitudeRadians = Math.Atan(Math.Sinh(mercatorCenterY / EarthRadiusMeters));
        var groundMetersPerPixel = mercatorResolution * Math.Cos(latitudeRadians);
        if (groundMetersPerPixel <= 0)
        {
            ClearBar();
            return;
        }

        var pick = PickSegmentation(groundMetersPerPixel, TargetPixelWidth);
        if (pick is null)
        {
            ClearBar();
            return;
        }

        BuildBar(pick.Value, groundMetersPerPixel);
    }

    private void ClearBar()
    {
        LabelsCanvas.Children.Clear();
        BarCanvas.Children.Clear();
        LabelsCanvas.Width = 0;
        BarCanvas.Width = 0;
    }

    private void BuildBar(SegmentationPick pick, double groundMetersPerPixel)
    {
        LabelsCanvas.Children.Clear();
        BarCanvas.Children.Clear();

        var segmentMeters = pick.SegmentLengthNm * MetersPerNauticalMile;
        var segmentPx = segmentMeters / groundMetersPerPixel;
        var totalPx = segmentPx * pick.SegmentCount;

        var accent = TryFindAccentBrush() ?? Brushes.SteelBlue;

        // Segments
        for (var i = 0; i < pick.SegmentCount; i++)
        {
            var rect = new Rectangle
            {
                Width = segmentPx,
                Height = BarCanvas.Height,
                Stroke = accent,
                StrokeThickness = 1,
                Fill = (i % 2 == 0) ? accent : Brushes.Transparent,
            };
            Canvas.SetLeft(rect, i * segmentPx);
            BarCanvas.Children.Add(rect);
        }

        // Labels (one at each tick: 0, 1*L, 2*L, ..., N*L)
        for (var i = 0; i <= pick.SegmentCount; i++)
        {
            var text = pick.FormatTick(i);
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
            };
            // Measure to center on the tick.
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = label.DesiredSize.Width;
            var tickX = i * segmentPx;
            Canvas.SetLeft(label, tickX - labelWidth / 2.0);
            LabelsCanvas.Children.Add(label);
        }

        // Pad the canvases so the outermost labels (which extend past the bar
        // ends) are not clipped.
        var firstLabelHalf = EstimateLabelHalfWidth(pick.FormatTick(0));
        var lastLabelHalf = EstimateLabelHalfWidth(pick.FormatTick(pick.SegmentCount));
        var leftPad = Math.Max(0, firstLabelHalf);
        var rightPad = Math.Max(0, lastLabelHalf);

        // Shift everything right by leftPad so x=0 stays inside the canvas.
        foreach (var child in BarCanvas.Children)
        {
            if (child is Control c)
            {
                Canvas.SetLeft(c, Canvas.GetLeft(c) + leftPad);
            }
        }
        foreach (var child in LabelsCanvas.Children)
        {
            if (child is Control c)
            {
                Canvas.SetLeft(c, Canvas.GetLeft(c) + leftPad);
            }
        }

        var canvasWidth = totalPx + leftPad + rightPad;
        BarCanvas.Width = canvasWidth;
        LabelsCanvas.Width = canvasWidth;
    }

    private IBrush? TryFindAccentBrush()
    {
        if (this.TryFindResource("AccentBrush", out var value) && value is IBrush brush)
            return brush;
        return null;
    }

    private static double EstimateLabelHalfWidth(string text)
    {
        // Approx width per character at 11pt; sufficient for padding.
        return text.Length * 4.0;
    }

    private static SegmentationPick? PickSegmentation(double groundMetersPerPixel, double targetPx)
    {
        var targetNm = targetPx * groundMetersPerPixel / MetersPerNauticalMile;
        if (targetNm <= 0 || double.IsInfinity(targetNm))
            return null;

        SegmentationPick? best = null;
        var bestScore = double.PositiveInfinity;

        foreach (var candidate in EnumerateSegmentLengths())
        {
            for (var n = 2; n <= 4; n++)
            {
                var totalNm = candidate.LengthNm * n;
                var totalPx = totalNm * MetersPerNauticalMile / groundMetersPerPixel;
                // Penalise candidates that fall too far from the target width;
                // use log-distance so over-shoot and under-shoot are weighted similarly.
                var score = Math.Abs(Math.Log(totalPx / targetPx));
                // Favour bars that don't grow too small to read or too wide to fit.
                if (totalPx < 60 || totalPx > 360)
                    score += 5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new SegmentationPick(candidate.LengthNm, n, candidate.Formatter);
                }
            }
        }

        return best;
    }

    private static IEnumerable<SegmentLengthCandidate> EnumerateSegmentLengths()
    {
        // Standard "1-2-2.5-5" nice numbers across many decades, plus a thirds
        // family for the zoomed-in halves/thirds/quarters case the user asked for.
        // 0.5 (=5e-1) and 0.25 (=2.5e-1) cover halves and quarters of 1 NM.
        for (var k = -4; k <= 6; k++)
        {
            var p = Math.Pow(10, k);
            yield return new SegmentLengthCandidate(1.0 * p, DecimalFormatter);
            yield return new SegmentLengthCandidate(2.0 * p, DecimalFormatter);
            yield return new SegmentLengthCandidate(2.5 * p, DecimalFormatter);
            yield return new SegmentLengthCandidate(5.0 * p, DecimalFormatter);

            if (k >= 0)
            {
                // Thirds family: 1/3, 10/3, 100/3, ... NM. Only enable for
                // p >= 1 so labels remain compact (e.g. avoid 0.0333 NM).
                yield return new SegmentLengthCandidate(p / 3.0, MakeThirdsFormatter(p));
            }
        }
    }

    private static string DecimalFormatter(int tickIndex, double segmentLengthNm)
    {
        var value = tickIndex * segmentLengthNm;
        // Trim trailing zeros, keep up to 4 decimals.
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static Func<int, double, string> MakeThirdsFormatter(double powerOfTen)
    {
        // Each tick is i * (powerOfTen / 3). Express as a mixed number
        // (whole + ⅓/⅔) when powerOfTen >= 1.
        return (tickIndex, _) =>
        {
            var numerator = tickIndex * (long)Math.Round(powerOfTen);
            var whole = numerator / 3;
            var remainder = numerator % 3;
            var wholeText = whole == 0 && remainder != 0 ? string.Empty : whole.ToString(CultureInfo.InvariantCulture);
            return remainder switch
            {
                0 => whole.ToString(CultureInfo.InvariantCulture),
                1 => wholeText + "\u2153", // ⅓
                2 => wholeText + "\u2154", // ⅔
                _ => whole.ToString(CultureInfo.InvariantCulture),
            };
        };
    }

    private readonly record struct SegmentLengthCandidate(double LengthNm, Func<int, double, string> Formatter);

    private readonly record struct SegmentationPick(double SegmentLengthNm, int SegmentCount, Func<int, double, string> Formatter)
    {
        public string FormatTick(int tickIndex) => Formatter(tickIndex, SegmentLengthNm);
    }
}
