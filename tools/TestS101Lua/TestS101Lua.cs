#:project ../../src/EncDotNet.S100.Datasets.S101/EncDotNet.S100.Datasets.S101.csproj
#:project ../../src/EncDotNet.S100.Features/EncDotNet.S100.Features.csproj
#:project ../../src/EncDotNet.S100.Scripting.MoonSharp/EncDotNet.S100.Scripting.MoonSharp.csproj

using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: dotnet run TestS101Lua.cs <dataset.000> <portrayal-catalogue-dir> <feature-catalogue.xml>");
    return 1;
}

string datasetPath = args[0];
string portrayalPath = args[1];
string featureCataloguePath = args[2];

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine($"Dataset not found: {datasetPath}");
    return 1;
}

if (!Directory.Exists(portrayalPath))
{
    Console.Error.WriteLine($"Portrayal catalogue not found: {portrayalPath}");
    return 1;
}

if (!File.Exists(featureCataloguePath))
{
    Console.Error.WriteLine($"Feature catalogue not found: {featureCataloguePath}");
    return 1;
}

// Open dataset
Console.WriteLine($"Opening dataset: {datasetPath}");
var dataset = S101Dataset.Open(datasetPath);
Console.WriteLine($"  Name: {dataset.DatasetName}");
Console.WriteLine($"  Features: {dataset.FeatureCount}");

// Open portrayal catalogue
Console.WriteLine($"Portrayal catalogue: {portrayalPath}");
var assetSource = FileSystemAssetSource.Create(portrayalPath);
var provider = await PortrayalCatalogueProvider.OpenAsync(assetSource);
Console.WriteLine($"  Rules: {provider.Catalogue.RuleFiles.Count}");
Console.WriteLine($"  Context parameters: {provider.Catalogue.ContextParameters.Count}");

// Read feature catalogue
Console.WriteLine($"Feature catalogue: {featureCataloguePath}");
var fc = FeatureCatalogueReader.Read(featureCataloguePath);
Console.WriteLine($"  Feature types: {fc.FeatureTypes.Count}");
Console.WriteLine($"  Simple attributes: {fc.SimpleAttributes.Count}");
Console.WriteLine($"  Complex attributes: {fc.ComplexAttributes.Count}");

// Set up navigation context
var context = new NavigationContext
{
    Viewport = new Viewport
    {
        MinLatitude = -90,
        MaxLatitude = 90,
        MinLongitude = -180,
        MaxLongitude = 180,
        WidthPixels = 1024,
        HeightPixels = 768,
    },
    ScaleDenominator = 0,
};

// Run Lua portrayal
Console.WriteLine();
Console.WriteLine("Running S-101 Lua portrayal pipeline...");
var luaEngine = new MoonSharpLuaEngine();
var portrayal = new S101LuaPortrayal(luaEngine, provider, fc);

var sw = System.Diagnostics.Stopwatch.StartNew();
var emitted = portrayal.Execute(dataset, context);
sw.Stop();

Console.WriteLine($"PortrayalMain completed in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  Emitted: {emitted.Count}");

// Analyze results
int withInstructions = 0;
int nullInstructions = 0;
int defaultSymbology = 0;
int properRules = 0;
var instructionTypes = new Dictionary<string, int>(StringComparer.Ordinal);

foreach (var e in emitted)
{
    if (string.IsNullOrEmpty(e.InstructionString))
        continue;

    withInstructions++;

    if (e.InstructionString.Contains("NullInstruction"))
        nullInstructions++;

    if (e.InstructionString.Contains("QUESMRK1"))
        defaultSymbology++;
    else if (!e.InstructionString.Contains("NullInstruction"))
        properRules++;

    // Count instruction types
    foreach (var segment in e.InstructionString.Split(';'))
    {
        var colonIdx = segment.IndexOf(':');
        var key = colonIdx >= 0 ? segment[..colonIdx] : segment;
        if (key is "PointInstruction" or "LineInstruction" or "LineInstructionUnsuppressed"
            or "AreaFillReference" or "ColorFill" or "TextInstruction" or "NullInstruction")
        {
            instructionTypes.TryGetValue(key, out var count);
            instructionTypes[key] = count + 1;
        }
    }
}

Console.WriteLine($"  With instructions: {withInstructions}");
Console.WriteLine($"  NullInstruction: {nullInstructions}");
Console.WriteLine($"  Default (QUESMRK1): {defaultSymbology}");
Console.WriteLine($"  Proper rules: {properRules}");
Console.WriteLine();

Console.WriteLine("Instruction type counts:");
foreach (var (type, count) in instructionTypes.OrderByDescending(kv => kv.Value))
{
    Console.WriteLine($"  {type}: {count}");
}

// Parse drawing instructions
Console.WriteLine();
var parsed = new List<ParsedDrawingInstruction>();
foreach (var e in emitted)
{
    parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
}
Console.WriteLine($"Parsed drawing instructions: {parsed.Count}");

// Show a few per type
var byType = parsed.GroupBy(p => p.Type).OrderByDescending(g => g.Count());
foreach (var group in byType)
{
    var sample = group.First();
    Console.WriteLine($"  {group.Key}: {group.Count()} (e.g. feature={sample.FeatureRef}, symbol={sample.SymbolRef ?? sample.Text ?? "-"})");
}

// Show first few errors (features that went to Default)
Console.WriteLine();
Console.WriteLine("First 5 features with Default (QUESMRK1) symbology:");
int shown = 0;
foreach (var e in emitted)
{
    if (e.InstructionString?.Contains("QUESMRK1") == true && shown < 5)
    {
        Console.WriteLine($"  Feature {e.FeatureRef}: {e.InstructionString[..Math.Min(120, e.InstructionString.Length)]}");
        shown++;
    }
}

return 0;
