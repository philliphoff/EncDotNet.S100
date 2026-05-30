// PR-D3 sample app: connects to aisstream.io, prints decoded AIS
// messages projected through AisDynamicFeatureSource. Demonstrates
// the three-layer split (driver → IAisMessageSource → dynamic
// feature source) without a UI dependency.
//
// Usage:
//   export ENCDOTNET_AIS_STREAM_KEY=<your-aisstream.io-key>
//   dotnet run --project samples/EncDotNet.S100.Samples.Ais
//
// Optional: pass "minLat,minLon,maxLat,maxLon" as the first arg to
// constrain the area (default = world).

using System.Globalization;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;

const string EnvVarName = "ENCDOTNET_AIS_STREAM_KEY";

var apiKey = Environment.GetEnvironmentVariable(EnvVarName);
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine($"error: environment variable '{EnvVarName}' is not set.");
    Console.Error.WriteLine($"Get a free API key from https://aisstream.io and run:");
    Console.Error.WriteLine($"  export {EnvVarName}=<key>");
    return 1;
}

BoundingBox? area = null;
if (args.Length > 0)
{
    var parts = args[0].Split(',', StringSplitOptions.TrimEntries);
    var inv = CultureInfo.InvariantCulture;
    if (parts.Length != 4
        || !double.TryParse(parts[0], NumberStyles.Float, inv, out var minLat)
        || !double.TryParse(parts[1], NumberStyles.Float, inv, out var minLon)
        || !double.TryParse(parts[2], NumberStyles.Float, inv, out var maxLat)
        || !double.TryParse(parts[3], NumberStyles.Float, inv, out var maxLon))
    {
        Console.Error.WriteLine("error: bbox must be 'minLat,minLon,maxLat,maxLon' (decimal degrees).");
        return 1;
    }
    area = new BoundingBox(minLat, minLon, maxLat, maxLon);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var driver = new AisStreamIoMessageSource(new AisStreamIoOptions { ApiKey = apiKey });
await using var source = new AisDynamicFeatureSource(
    id: "ais",
    messageSource: driver,
    request: new AisSubscriptionRequest { Area = area });

var seen = new HashSet<string>();
source.Changed += (sender, change) =>
{
    var src = (IDynamicFeatureSource)sender!;
    var byId = src.CurrentFeatures.ToDictionary(f => f.Id);

    foreach (var id in change.ChangedIds)
    {
        switch (change.Kind)
        {
            case DynamicSourceChangeKind.Added:
                seen.Add(id);
                if (byId.TryGetValue(id, out var addedFeat))
                    Console.WriteLine($"+ {Format(addedFeat)}");
                break;

            case DynamicSourceChangeKind.Updated:
                if (byId.TryGetValue(id, out var updatedFeat))
                    Console.WriteLine($"  {Format(updatedFeat)}");
                break;

            case DynamicSourceChangeKind.Removed:
                seen.Remove(id);
                Console.WriteLine($"- {id}");
                break;
        }
    }
};

var bboxLabel = area is null ? "world" : $"[{area.SouthLatitude:F2},{area.WestLongitude:F2} → {area.NorthLatitude:F2},{area.EastLongitude:F2}]";
Console.WriteLine($"Connected to aisstream.io, listening on {bboxLabel}. Press Ctrl+C to exit.");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // graceful exit
}

Console.WriteLine();
Console.WriteLine($"Disconnected. Saw {seen.Count} unique vessels.");
return 0;

static string Format(DynamicFeature f)
{
    var (lat, lon) = f.Coordinates.Count > 0 ? f.Coordinates[0] : (0.0, 0.0);
    var sog = f.Motion?.SpeedOverGroundKn?.ToString("0.0") ?? "?";
    var cog = f.Motion?.CourseOverGroundDeg?.ToString("0") ?? "?";
    var kind = f.Kind ?? "?";
    return $"{f.Id,-16} {kind,-22} {lat,8:F4},{lon,9:F4} SOG={sog,5}kn COG={cog,3}°";
}
