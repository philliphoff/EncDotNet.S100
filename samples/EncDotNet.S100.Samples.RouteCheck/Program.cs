// EncDotNet.S100 route check sample.
//
// Loads a set of datasets, describes a passage, runs every route check on it
// with RouteChecker and prints the verdict, what each check did and the
// findings. See docs/how-a-route-check-works.md.
//
// Run with the bundled synthetic data (generated into a temporary folder):
//     dotnet run --project samples/EncDotNet.S100.Samples.RouteCheck
//
// Or point it at your own data folder (an exchange set or loose dataset
// files) and an RTZ route:
//     dotnet run --project samples/EncDotNet.S100.Samples.RouteCheck -- path/to/data path/to/route.rtz

using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Testing.Scenarios;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string? generated = null;
string dataFolder;
if (args.Length > 0)
{
    dataFolder = args[0];
}
else
{
    // In-repo only: write the synthetic route showcase (a fictitious sound
    // with charts, S-102, S-104, S-122 and S-124) into a temporary folder.
    generated = Path.Combine(Path.GetTempPath(), "s100-route-check-sample-" + Guid.NewGuid().ToString("N"));
    ScenarioGenerator.Generate(RouteShowcase.Definition(), generated);
    dataFolder = generated;
}

try
{
    // 1. Load every dataset in the folder. The product is detected from each file.
    var transforms = new ProjNetCrsTransformFactory();
    var inputs = Directory.EnumerateFiles(dataFolder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => (Path: path, Spec: DatasetPipelineFactory.DetectProductSpec(path)))
        .Where(file => file.Spec is not null)
        .Select(file => new FileDatasetInput(
            new DatasetId(Path.GetRelativePath(dataFolder, file.Path)), file.Spec!, file.Path));
    var catalog = FileDatasetCatalog.Build(inputs, transforms);
    Console.WriteLine($"Loaded {catalog.Datasets.Count} datasets from {dataFolder}");

    // 2. Gather what the checks read: charts, depths, water levels, areas and
    //    warnings. Build the sources and the checker once per set of datasets.
    var sources = CatalogRouteCheckSources.FromDatasets(catalog.Datasets, transforms);
    var checker = new RouteChecker(sources);

    // 3. Describe the passage: from an RTZ file, or by hand.
    var plan = args.Length > 1 ? RtzReader.Read(args[1]).ToPassagePlan() : SamplePlan();
    Console.WriteLine($"Passage: {plan.Legs.Count} legs, departing {plan.Departure:u}");

    // 4. Check it. The safety contour and safety depth turn on the depth and
    //    danger checks; At is used only where the plan gives no time.
    var report = checker.Check(plan, new RouteCheckSettings
    {
        SafetyContour = Depth.FromMetres(10),
        SafetyDepth = Depth.FromMetres(10),
        At = plan.Departure ?? DateTimeOffset.UtcNow,
    });

    // 5. Print the verdict, what each check did, and the findings.
    Console.WriteLine();
    Console.WriteLine($"Verdict: {report.Verdict.Kind}" +
        (report.Verdict.Headline is { } headline ? $" ({headline.Title})" : ""));
    Console.WriteLine($"Findings: {report.Counts.MustReview} must review, {report.Counts.Check} check, {report.Counts.Info} info");

    Console.WriteLine();
    Console.WriteLine("Checks:");
    foreach (var check in report.Checks)
    {
        Console.WriteLine($"  {check.Name,-10} {check.State,-8} {check.FindingCount,3} findings {check.Reason}".TrimEnd());
    }

    Console.WriteLine();
    Console.WriteLine("Findings:");
    foreach (var finding in report.Findings)
    {
        var leg = finding.LegIndex is { } index ? $"leg {index + 1}" : "";
        var distance = finding.Distance is { } d ? $"{d.TotalMetres:F0} m" : "";
        Console.WriteLine($"  {finding.Group,-10} {finding.Code.Value,-40} {leg,-6} {distance,8}  {finding.Title}");
    }

    if (report.Clearance is { } clearance)
    {
        Console.WriteLine();
        Console.WriteLine($"Clearance: {clearance.Verdict}" + (clearance.Minimum is { } minimum
            ? $", smallest UKC {minimum.ClearanceMeters:F2} m at {minimum.Time:u}"
            : ""));
    }

    return 0;
}
finally
{
    if (generated is not null)
    {
        Directory.Delete(generated, recursive: true);
    }
}

// A two-leg passage through the synthetic sound: east past charted dangers,
// then on along the channel to the berth, at 8 knots with a 7 m draft.
static PassagePlan SamplePlan()
{
    var west = new GeoPosition(-40.97036, -134.99405);
    var mid = new GeoPosition(-40.97036, -134.96429);
    var berth = new GeoPosition(-40.97260, -134.93215);

    PassageLeg Leg(GeoPosition from, GeoPosition to) => new()
    {
        Points = [from, to],
        PortLimit = Length.FromMetres(100),
        StarboardLimit = Length.FromMetres(100),
        Speed = Speed.FromKnots(8),
        Draft = Depth.FromMetres(7.0),
        Squat = Depth.FromMetres(0.4),
        RequiredUkc = Depth.FromMetres(1.0),
    };

    return new PassagePlan([Leg(west, mid), Leg(mid, berth)], new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));
}
