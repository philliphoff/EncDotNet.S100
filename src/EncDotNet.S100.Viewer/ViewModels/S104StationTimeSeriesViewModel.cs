using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Station time-series view model for an S-104 water-level station. Exposes
/// a single height-vs-time line series via <see cref="HeightSeries"/>.
/// </summary>
internal sealed class S104StationTimeSeriesViewModel : StationTimeSeriesViewModel
{
    public S104StationTimeSeriesViewModel(StationTimeSeriesSnapshot snapshot, GlobalTimeService? globalTime)
        : base(snapshot, globalTime)
    {
        var channel = FindChannel(snapshot, "waterLevelHeight");
        var points = channel is null ? new List<DateTimePoint>() : ProjectChannel(snapshot.Times, channel);

        HeightSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Name = Strings.Pick_Chart_Title_WaterLevel,
                Values = points,
                Stroke = new SolidColorPaint(new SKColor(0x1F, 0x77, 0xB4), 2f),
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
            },
        };

        HeightAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_HeightMetres,
        };
        HeightAxisArray = new ICartesianAxis[] { HeightAxis };
    }

    /// <summary>Single height-vs-time series for the S-104 chart.</summary>
    public ISeries[] HeightSeries { get; }

    /// <summary>Y axis for the height chart (metres).</summary>
    public Axis HeightAxis { get; }

    /// <summary>Single-element wrapper around <see cref="HeightAxis"/>.</summary>
    public ICartesianAxis[] HeightAxisArray { get; }

    /// <summary>Convenience accessor: title for the chart.</summary>
    public string Title => Strings.Pick_Chart_Title_WaterLevel;

    private static StationTimeSeriesChannel? FindChannel(StationTimeSeriesSnapshot snapshot, string key)
    {
        foreach (var c in snapshot.Channels)
        {
            if (string.Equals(c.Key, key, System.StringComparison.Ordinal))
                return c;
        }
        return null;
    }
}
