#:package SkiaSharp@3.119.0
#:project ../../src/EncDotNet.S100.Datasets.S102/EncDotNet.S100.Datasets.S102.csproj
#:project ../../src/EncDotNet.S100.Renderers.Skia/EncDotNet.S100.Renderers.Skia.csproj
#:project ../../src/EncDotNet.S100.Scripting.MoonSharp/EncDotNet.S100.Scripting.MoonSharp.csproj

using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Scripting.MoonSharp;
using SkiaSharp;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: dotnet run RenderS102.cs <input.h5> <output.png> <portrayal-catalogue-dir>");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];
string portrayalPath = args[2];

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

if (!Directory.Exists(portrayalPath))
{
    Console.Error.WriteLine($"Portrayal catalogue directory not found: {portrayalPath}");
    return 1;
}

// 1. Read the S-102 dataset from HDF5
Console.WriteLine($"Reading {inputPath}...");
using var hdf5 = PureHdfFile.Open(inputPath);
var dataset = S102DatasetReader.Read(hdf5);

Console.WriteLine($"  Geographic ID : {dataset.GeographicIdentifier ?? "(none)"}");
Console.WriteLine($"  Horizontal CRS: {dataset.HorizontalCRS?.ToString() ?? "unknown"}");
Console.WriteLine($"  Coverages     : {dataset.Coverages.Count}");

var coverage = dataset.Coverages[0];
Console.WriteLine($"  Grid size     : {coverage.NumPointsLongitudinal} x {coverage.NumPointsLatitudinal}");
Console.WriteLine($"  Origin        : ({coverage.OriginLatitude:F6}, {coverage.OriginLongitude:F6})");

// 2. Create the pipeline components
var source = new S102CoverageSource(dataset);
var engine = new MoonSharpLuaEngine();
using var assetSource = new FileSystemAssetSource(portrayalPath);
using var provider = await PortrayalCatalogueProvider.OpenAsync(assetSource);
var catalogue = new S102PortrayalCatalogue(engine, provider) { FourShades = true };
var pipeline = new CoveragePipeline();

// 3. Run the pipeline
Console.WriteLine("Running coverage pipeline...");
var layer = await pipeline.ProcessAsync(source, catalogue);

// 4. Build the styled layer for rendering
var colorScheme = catalogue.ResolveColorScheme(
    new NavigationContext
    {
        Viewport = new Viewport
        {
            MinLatitude = source.Metadata.Extent.SouthLatitude,
            MaxLatitude = source.Metadata.Extent.NorthLatitude,
            MinLongitude = source.Metadata.Extent.WestLongitude,
            MaxLongitude = source.Metadata.Extent.EastLongitude,
            WidthPixels = source.Metadata.GridMetadata.NumColumns,
            HeightPixels = source.Metadata.GridMetadata.NumRows,
        },
        ScaleDenominator = 50_000,
    });

var styledLayer = new StyledCoverageLayer
{
    Coverage = source.Sample(GridRegion.Full),
    ColorScheme = colorScheme,
    NoDataValue = S102CoverageSource.FillValue,
    Georeferencer = new GridGeoreferencer(
        source.Metadata.GridMetadata,
        source.Metadata.HorizontalCRS),
};

// 5. Render to bitmap
Console.WriteLine("Rendering...");
var renderer = new SkiaCoverageRenderer();
var viewport = new Viewport
{
    MinLatitude = source.Metadata.Extent.SouthLatitude,
    MaxLatitude = source.Metadata.Extent.NorthLatitude,
    MinLongitude = source.Metadata.Extent.WestLongitude,
    MaxLongitude = source.Metadata.Extent.EastLongitude,
    WidthPixels = source.Metadata.GridMetadata.NumColumns,
    HeightPixels = source.Metadata.GridMetadata.NumRows,
};

using var bitmap = renderer.Render(styledLayer, viewport);

// 6. Encode and save
using var image = SKImage.FromBitmap(bitmap);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
using var stream = File.OpenWrite(outputPath);
data.SaveTo(stream);

Console.WriteLine($"Wrote {bitmap.Width}x{bitmap.Height} PNG to {outputPath}");
return 0;
