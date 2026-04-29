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

// Set up mariner settings
var mariner = MarinerSettings.Default;

// Run Lua portrayal
Console.WriteLine();
Console.WriteLine("Running S-101 Lua portrayal pipeline...");
var luaEngine = new MoonSharpLuaEngine();
var portrayal = new S101LuaRuleExecutor(luaEngine, dataset, provider, fc);

var sw = System.Diagnostics.Stopwatch.StartNew();
var emitted = portrayal.ExecuteRaw(mariner);
sw.Stop();

Console.WriteLine($"PortrayalMain completed in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  Emitted: {emitted.Count}");

// Dump first few raw instruction strings containing LineStyle
Console.WriteLine();
Console.WriteLine("First 3 raw instruction strings containing LineStyle:");
int rawShown = 0;
foreach (var e in emitted)
{
    if (rawShown >= 3) break;
    if (e.InstructionString?.Contains("LineStyle") == true)
    {
        Console.WriteLine($"  Feature {e.FeatureRef}: {e.InstructionString}");
        rawShown++;
    }
}

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

// ── Detailed diagnostics: geometry + instructions per feature ──
Console.WriteLine();
Console.WriteLine("=== DETAILED FEATURE DIAGNOSTICS ===");

// Get feature geometry from the vector source
var vectorSource = new S101VectorSource(dataset);
var geoFeatures = vectorSource.GetFeatures();
var geoLookup = new Dictionary<long, (string FeatureType, string GeomType, int CoordCount, double MinLat, double MaxLat, double MinLon, double MaxLon)>();
foreach (var gf in geoFeatures)
{
    double minLat = double.MaxValue, maxLat = double.MinValue;
    double minLon = double.MaxValue, maxLon = double.MinValue;
    foreach (var (lat, lon) in gf.Coordinates)
    {
        if (lat < minLat) minLat = lat;
        if (lat > maxLat) maxLat = lat;
        if (lon < minLon) minLon = lon;
        if (lon > maxLon) maxLon = lon;
    }
    geoLookup[gf.Id] = (gf.FeatureType, gf.GeometryType.ToString(), gf.Coordinates.Count,
        minLat, maxLat, minLon, maxLon);
}

Console.WriteLine($"Features with geometry: {geoLookup.Count} of {dataset.FeatureCount}");
Console.WriteLine();

// Overall coordinate extent
if (geoLookup.Count > 0)
{
    double oMinLat = geoLookup.Values.Min(g => g.MinLat);
    double oMaxLat = geoLookup.Values.Max(g => g.MaxLat);
    double oMinLon = geoLookup.Values.Min(g => g.MinLon);
    double oMaxLon = geoLookup.Values.Max(g => g.MaxLon);
    Console.WriteLine($"Overall extent: Lat [{oMinLat:F6}, {oMaxLat:F6}], Lon [{oMinLon:F6}, {oMaxLon:F6}]");
    Console.WriteLine();
}

// Group emitted by feature, show full instructions + geometry
Console.WriteLine("Per-feature detail (first 20 features with proper rules):");
int detailShown = 0;
foreach (var e in emitted)
{
    if (string.IsNullOrEmpty(e.InstructionString)) continue;
    if (e.InstructionString.Contains("QUESMRK1")) continue;  // skip defaults
    if (e.InstructionString.Contains("NullInstruction")) continue;
    if (detailShown >= 20) break;
    detailShown++;

    Console.WriteLine($"  Feature {e.FeatureRef}:");
    if (long.TryParse(e.FeatureRef, out var fid) && geoLookup.TryGetValue(fid, out var geo))
    {
        Console.WriteLine($"    Type: {geo.FeatureType}, Geom: {geo.GeomType}, Coords: {geo.CoordCount}");
        Console.WriteLine($"    Extent: Lat [{geo.MinLat:F6}, {geo.MaxLat:F6}], Lon [{geo.MinLon:F6}, {geo.MaxLon:F6}]");
    }
    else
    {
        Console.WriteLine($"    ** No geometry found **");
    }

    // Show parsed instructions for this feature
    var featureParsed = parsed.Where(p => p.FeatureRef == e.FeatureRef).ToList();
    foreach (var p in featureParsed)
    {
        var detail = p.Type switch
        {
            InstructionType.AreaFill => $"AreaFill: symbol={p.SymbolRef}, colorFill={p.IsColorFill}, transparency={p.Transparency}",
            InstructionType.Line => $"Line: symbol={p.SymbolRef}, color={p.LineColor}, width={p.LineWidth}",
            InstructionType.Point => $"Point: symbol={p.SymbolRef}, rotation={p.Rotation}, scale={p.ScaleFactor}",
            InstructionType.Text => $"Text: \"{p.Text}\", color={p.FontColor}, size={p.FontSize}",
            _ => p.Type.ToString(),
        };
        Console.WriteLine($"    [{p.Type}] VG={p.ViewingGroup} DP={p.DrawingPriority} Plane={p.DisplayPlane} {detail}");
    }
    Console.WriteLine();
}

// Show geometry type distribution
Console.WriteLine("Geometry type distribution:");
foreach (var group in geoLookup.Values.GroupBy(g => g.GeomType).OrderByDescending(g => g.Count()))
{
    Console.WriteLine($"  {group.Key}: {group.Count()}");
}

// Show features with instructions but no geometry
Console.WriteLine();
var missingGeo = emitted
    .Where(e => !string.IsNullOrEmpty(e.InstructionString) && !e.InstructionString.Contains("NullInstruction"))
    .Where(e => long.TryParse(e.FeatureRef, out var fid2) && !geoLookup.ContainsKey(fid2))
    .ToList();
Console.WriteLine($"Features with instructions but NO geometry: {missingGeo.Count}");
foreach (var e in missingGeo.Take(5))
{
    Console.WriteLine($"  Feature {e.FeatureRef}: {e.InstructionString[..Math.Min(80, e.InstructionString.Length)]}");
}

return 0;
