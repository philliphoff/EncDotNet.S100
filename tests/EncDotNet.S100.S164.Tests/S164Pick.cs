using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// Picks features in an S-101 cell from an S-164 exchange set, for the Part 4
/// feature-information tests: <see cref="IdentifyFeaturesService"/> finds what
/// is under the cursor, as <c>s100 identify</c> and the MCP
/// <c>identify_features</c> tool do, and each match is reported through
/// <see cref="S101DatasetProcessor.GetFeatureInfo"/> (the viewer's pick report)
/// and <see cref="DescribeFeatureService"/> (<c>describe_feature</c>).
/// </summary>
internal sealed class S164Pick
{
    private readonly IdentifyFeaturesService _identify;
    private readonly DescribeFeatureService _describe;

    private S164Pick(S101DatasetProcessor processor, LoadedDataset dataset, IReadOnlyList<DrawingInstruction> instructions)
    {
        Processor = processor;
        Dataset = dataset;
        Instructions = instructions;
        var catalog = new Catalog([dataset]);
        _identify = new IdentifyFeaturesService(catalog);
        _describe = new DescribeFeatureService(catalog);
    }

    /// <summary>The cell's processor.</summary>
    public S101DatasetProcessor Processor { get; }

    /// <summary>The cell as the pick services see it.</summary>
    public LoadedDataset Dataset { get; }

    /// <summary>The cell's drawing instructions at the display tests' settings, with every display category on.</summary>
    public IReadOnlyList<DrawingInstruction> Instructions { get; }

    /// <summary>Opens the cell <paramref name="cell"/> of the exchange set <paramref name="exchangeSet"/>.</summary>
    public static async Task<S164Pick> OpenAsync(string exchangeSet, string cell, CancellationToken cancellationToken)
    {
        var (processor, instructions) = await S164Portrayal.PortrayAsync(
            exchangeSet, cell, S164Portrayal.DisplayTestSettings, cancellationToken);
        var dataset = LoadedDatasetProjector.Project(new DatasetId(cell), processor)
            ?? throw new InvalidOperationException($"'{exchangeSet}' cell {cell} didn't load.");
        return new S164Pick(processor, dataset, instructions);
    }

    /// <summary>The cell's features of type <paramref name="featureType"/>.</summary>
    public IEnumerable<IS100Feature> Features(string featureType) =>
        FeatureAccessor.GetFeatures(Dataset)!.Where(f => f.FeatureType == featureType);

    /// <summary>The position of a point feature, or the middle vertex of a curve, or the centre of an area's outline.</summary>
    public static GeoPosition PositionOf(IS100Feature feature)
    {
        if (feature.Points.Count > 0)
        {
            return feature.Points[0];
        }

        if (feature.Curves.Count > 0)
        {
            return feature.Curves[0][feature.Curves[0].Count / 2];
        }

        var ring = feature.ExteriorRing;
        return new GeoPosition(ring.Average(p => p.Latitude), ring.Average(p => p.Longitude));
    }

    /// <summary>The features under the cursor at <paramref name="position"/>, in the order <c>identify_features</c> ranks them.</summary>
    public async Task<IReadOnlyList<IdentifyMatch>> PickAsync(GeoPosition position, double radiusMeters = 20)
    {
        var result = await _identify.InvokeAsync(
            new IdentifyFeaturesRequest(position.Latitude, position.Longitude, RadiusMeters: radiusMeters, MaxResults: 200),
            TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var picked), "identify_features failed.");
        return picked!.Features;
    }

    /// <summary>The pick report of the feature <paramref name="featureId"/>.</summary>
    public FeatureInfo Report(string featureId) =>
        Processor.GetFeatureInfo(featureId) ?? throw new InvalidOperationException($"No feature {featureId}.");

    /// <summary>The <c>describe_feature</c> payload of the feature <paramref name="featureId"/>.</summary>
    public async Task<JsonElement> DescribeAsync(string featureId)
    {
        var result = await _describe.InvokeAsync(
            new DescribeFeatureRequest(Dataset.Id, featureId), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var described), "describe_feature failed.");
        return described!.Attributes;
    }

    /// <summary>Every row of a pick report, complex attributes' children included.</summary>
    public static IEnumerable<PickAttribute> Rows(FeatureInfo report) => report.Attributes.SelectMany(Rows);

    private static IEnumerable<PickAttribute> Rows(PickAttribute row) => row.Children.SelectMany(Rows).Prepend(row);

    private sealed class Catalog(IReadOnlyList<LoadedDataset> datasets) : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => datasets;

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
