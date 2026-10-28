using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The colour legend next to a weather fill in the Layer Stack (#1072): the
/// view model reads <see cref="GridCoverageSubLayer.Legend"/>, and the view
/// shows a named swatch and label per band.
/// </summary>
public sealed class LayerStackLegendTests
{
    private static readonly CoverageLegendEntry[] WaveLegend =
    [
        new("#F3EEFF", "0–0.5 m"),
        new("#E5DFFC", "0.5–1 m"),
        new("#48198A", "14 m and higher"),
    ];

    [Fact]
    public void A_fill_with_a_legend_gets_one_row_per_band_lowest_first()
    {
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FillEntry("gfswave.grib2", WaveLegend));

        Assert.True(entry.HasLegend);
        Assert.Equal(WaveLegend.Select(e => e.Label), entry.Legend.Select(i => i.Label));
        var swatch = Assert.IsAssignableFrom<ISolidColorBrush>(entry.Legend[0].Swatch);
        Assert.Equal(Color.Parse("#F3EEFF"), swatch.Color);
        Assert.Equal("Legend of Wave height: gfswave.grib2, 3 bands", entry.LegendName);
    }

    [Fact]
    public void Entries_without_a_legend_have_none()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();

        var plain = new LayerStackEntryViewModel(loader, new LayerStackEntry(
            new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")));
        var unlabelled = new LayerStackEntryViewModel(loader, FillEntry("s104.h5", legend: null));

        Assert.False(plain.HasLegend);
        Assert.Empty(plain.Legend);
        Assert.False(unlabelled.HasLegend);
    }

    [Fact]
    public void An_unparseable_colour_draws_a_transparent_swatch()
    {
        var item = new LayerStackLegendItemViewModel("not a colour", "Calm");

        Assert.Same(Brushes.Transparent, item.Swatch);
        Assert.Equal("Calm", item.Label);
    }

    [AvaloniaFact]
    public void The_view_shows_a_named_legend_with_a_named_row_per_band()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(
            FillEntry("gfswave.grib2", WaveLegend),
            new LayerStackEntry(new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);

        // Only the weather fill has a legend.
        var legend = host.FindAll<ItemsControl>("LayerStack.Entry.Legend").Where(c => c.IsVisible).ToArray();
        var shown = Assert.Single(legend);
        Assert.Equal("Legend of Wave height: gfswave.grib2, 3 bands", AutomationProperties.GetName(shown));

        var bands = host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band");
        Assert.Equal(WaveLegend.Select(e => e.Label), bands.Select(AutomationProperties.GetName));
        Assert.All(bands, band => Assert.True(band.IsVisible));
    }

    [AvaloniaFact]
    public void The_legend_follows_a_rebuild_of_the_stack()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(FillEntry("gfswave.grib2", WaveLegend));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);
        Assert.Equal(3, host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band").Count);

        // Redrawn at Night: new colours, same bands.
        loader.SetEntries(FillEntry("gfswave.grib2", WaveLegend.Select(e => e with { Color = "#171322" }).ToArray()));
        loader.FireLayerStackChanged();
        host.Settle();

        var entry = (LayerStackEntryViewModel)Assert.Single(Assert.Single(stack.Planes).Children);
        Assert.All(entry.Legend, item => Assert.Equal("#171322", item.Color));
        Assert.Equal(3, host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band").Count);
    }

    // A weather wave height fill on the on-demand surface, with or without a legend.
    internal static LayerStackEntry FillEntry(string datasetId, IReadOnlyList<CoverageLegendEntry>? legend)
    {
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 0,
            OriginLongitude = 0,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        var fill = new GridCoverageSubLayer
        {
            LayerKey = "weather.wave.height",
            LayerName = $"Wave height: {datasetId}",
            Plane = S98DisplayPlane.OnDemandSurface,
            SourceFeatureType = "weather.wave.height",
            Coverage = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["wave.hs"] = [1f, 2f, 3f, 4f] },
                },
                NoDataValue = float.NaN,
                Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            },
            Viewport = new Viewport
            {
                MinLatitude = 0,
                MaxLatitude = 1,
                MinLongitude = 0,
                MaxLongitude = 1,
                WidthPixels = 1,
                HeightPixels = 1,
                ScaleDenominator = 1,
            },
            Opacity = 0.5,
            Legend = legend,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = [fill],
            Spec = new SpecRef("GRIB2", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new CoverageStackPayload(result, fill), fill.Plane, 0, datasetId, fill.SourceFeatureType));
    }
}
