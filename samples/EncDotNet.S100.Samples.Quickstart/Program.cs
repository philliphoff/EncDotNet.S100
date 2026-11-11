// EncDotNet.S100 quickstart sample.
//
// Demonstrates the batteries-included facade end-to-end: open a dataset, read
// its features through the bundled feature catalogue, and render it to a PNG
// through the bundled portrayal catalogue — no hand-wiring of catalogues or
// pipelines.
//
// Run with the bundled synthetic S-124 fixture:
//     dotnet run --project samples/EncDotNet.S100.Samples.Quickstart
//
// Or point it at your own dataset (ISO 8211 .000, HDF5 .h5, or GML):
//     dotnet run --project samples/EncDotNet.S100.Samples.Quickstart -- path/to/dataset out.png

using EncDotNet.S100;

string datasetPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "sample-navwarn.gml");
string outputPath = args.Length > 1 ? args[1] : "out.png";

// 1. Open the dataset. The product specification is detected from the file.
using var dataset = S100Dataset.Open(datasetPath);
Console.WriteLine($"Opened {Path.GetFileName(datasetPath)} — {dataset.Spec}");

// 2. Enumerate features through the bundled feature catalogue (empty for
//    coverage products such as S-102/104/111).
using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);
var features = featureCatalogue.EnumerateFeatures(dataset);
Console.WriteLine($"Features: {features.Count}");
foreach (var feature in features)
    Console.WriteLine($"  {feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");

// 3. Render to PNG using the bundled portrayal catalogue.
if (dataset.CanRenderHeadless)
{
    using var renderer = new PngS100DatasetRenderer();
    byte[] png = await renderer.RenderAsync(dataset);
    File.WriteAllBytes(outputPath, png);
    Console.WriteLine($"Wrote {png.Length:N0} bytes to {outputPath}");
}
else
{
    Console.WriteLine("This dataset shape cannot be rendered headlessly; skipping PNG output.");
}
