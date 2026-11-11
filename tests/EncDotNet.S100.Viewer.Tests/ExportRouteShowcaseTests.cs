using System.Globalization;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Testing.Scenarios.Writers;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Exchange;
using EncDotNet.S100.Viewer.Routing.Persistence;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Writes the <see cref="RouteShowcase"/> for a look at the route UX in
/// SoundCharts: the exchange set, a Library manifest, an RTZ route and a
/// data folder whose <c>routes.json</c> already holds every route. Runs only
/// when <c>S100_ROUTE_SHOWCASE_OUT</c> names the folder.
/// </summary>
public sealed class ExportRouteShowcaseTests
{
    private const string Variable = "S100_ROUTE_SHOWCASE_OUT";

    [Fact]
    public async Task Writes_the_route_showcase_when_asked()
    {
        var root = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrEmpty(root), $"Set {Variable} to a folder to write the route showcase there.");
        root = Path.GetFullPath(root!);

        var exchangeSet = Path.Combine(root, "exchange-set");
        var dataDir = Path.Combine(root, "data-dir");
        var received = Path.Combine(root, "received");
        foreach (var folder in new[] { exchangeSet, dataDir, received })
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
        }

        var definition = RouteShowcase.Definition();
        var scenario = ScenarioGenerator.Generate(definition, exchangeSet);

        // The received RTZ route (Import route…, reopened at start-up).
        var rtzPath = Path.Combine(received, RouteShowcase.RtzRoute.DatasetName + ".rtz");
        var rtz = RouteS421Mapping.FromS421(S421RouteBuilder.Build(definition, RouteShowcase.RtzRoute));
        await using (var stream = File.Create(rtzPath))
        {
            new RtzRouteFormat().Write(rtz, new RouteExchangeIdentity(RouteShowcase.RtzRoute.RouteId, 1, RouteExchangeIdentity.StatusPlanned, null, null, Kept: false),
                new RouteExportOptions(IncludeCheckSettings: true), stream);
        }

        // Every showcase route as an owned route, plus the edit states.
        var routes = new RouteCollection();
        foreach (var showcase in RouteShowcase.Routes)
        {
            var route = RouteS421Mapping.FromS421(S421RouteBuilder.Build(definition, showcase.Route), id: showcase.Route.RouteId);
            if (showcase.Route.WritesDraft)
                route.SetPassageValue(RoutePassageField.Squat, showcase.Route.Passage.SquatMeters);
            routes.Add(route);
        }

        routes.Add(new Route("ZZ00-SHOWCASE-18", "18 New route with no waypoints"));
        var single = new Route("ZZ00-SHOWCASE-19", "19 One waypoint, no legs yet");
        single.AppendWaypoint(definition.Place("clear-west"));
        routes.Add(single);

        // A copy to edit of the morning channel transit, as Copy to edit makes it.
        var source = scenario.Datasets.Single(d => d.FullPath.EndsWith("421ZZ00WARN.gml", StringComparison.Ordinal)).FullPath;
        var receivedRoute = new S421RouteFormat().ReadReceived(source);
        var copy = new Route("ZZ00-SHOWCASE-20", "20 Copy to edit of 12 (provenance kept)");
        var snapshot = receivedRoute.CaptureSnapshot();
        snapshot.Info.Name = copy.Name;
        copy.RestoreSnapshot(snapshot);
        copy.Origin = receivedRoute.Source;
        copy.Preserved = receivedRoute.Preserved;
        routes.Add(copy);
        routes.SetActiveRoute(routes.Routes[0]);
        RouteStore.Save(routes, Path.Combine(dataDir, "routes.json"));

        // Mariner depths for a 5–15 m draft in 13–17 m of water: the default
        // 30 m safety contour would make every route cross it.
        await File.WriteAllTextAsync(Path.Combine(dataDir, "settings.json"),
            JsonSerializer.Serialize(new { ShallowContour = 5.0, SafetyContour = 10.0, SafetyDepth = 10.0, DeepContour = 20.0 }),
            TestContext.Current.CancellationToken);

        var checkSettings = new CheckSettingsStore(Path.Combine(dataDir, "check-settings.json"));
        checkSettings.SetOpenFiles([rtzPath]);
        checkSettings.Save();

        // A Library manifest over the exchange set, grouped by what the products are for.
        var manifest = Path.Combine(root, "route-showcase.s100collection.json");
        await File.WriteAllTextAsync(manifest, Manifest(), TestContext.Current.CancellationToken);
        using (var library = LibraryServices.Create(new LibraryDataPaths(dataDir)))
        {
            library.Library.Initialize();
            var draft = LibrarySourceDraft.ForPath(LibrarySourceKind.LocalManifest, manifest);
            Assert.Null(await draft.LoadAsync(TestContext.Current.CancellationToken));
            draft.AddTo(library.Library);
            await library.Library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }

        // This indexer has no product probe; the viewer indexes the collection
        // itself at start-up.
        Directory.Delete(new LibraryDataPaths(dataDir).IndexCacheDirectory, recursive: true);

        // One command opens SoundCharts with every dataset but the routes loaded.
        var script = Path.Combine(root, "open-showcase.sh");
        await File.WriteAllTextAsync(script, LaunchScript(root, scenario), TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, File.GetUnixFileMode(script) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), Readme(root), TestContext.Current.CancellationToken);
        Assert.True(File.Exists(scenario.CataloguePath));
    }

    private static string Manifest()
    {
        return JsonSerializer.Serialize(
            new
            {
                format = "encdotnet-s100-collection",
                version = 1,
                title = "Route showcase",
                description = "SYNTHETIC route UX showcase in Synthetic Sound, a fictitious area. Not for navigation.",
                // The catalogue lists every dataset; the product folders alone
                // would leave out the HDF5 files the Library can't probe loose.
                groups = new[] { new { id = "exchange-set", name = "Route showcase exchange set", paths = new[] { "exchange-set" } } },
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private const string Bbox = "-40.978,-135.0,-40.94,-134.925";

    private static string LaunchScript(string root, GeneratedScenario scenario)
    {
        var repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "EncDotNet.S100.slnx")))
            repo = Path.GetDirectoryName(repo) ?? throw new InvalidOperationException("No EncDotNet.S100.slnx above the test output.");

        var datasets = scenario.Datasets.Where(d => d.ProductIdentifier != "S-421").Select(d => $" \"{d.FullPath}\"");
        return $"""
            #!/bin/sh
            # Opens SoundCharts on the route showcase with every dataset but the routes loaded.
            # SYNTHETIC test data, not for navigation. Generated by ExportRouteShowcaseTests.
            cd "{repo}" || exit 1
            exec dotnet run --project src/EncDotNet.S100.Viewer -c Release -- --data-dir "{Path.Combine(root, "data-dir")}" --bbox {Bbox}{string.Concat(datasets)}

            """;
    }

    private static string Readme(string root)
    {
        var text = new StringBuilder();
        text.AppendLine("# Route showcase (SYNTHETIC, not for navigation)");
        text.AppendLine();
        text.AppendLine("Every principal route state side by side in Synthetic Sound (41° S 135° W, fictitious).");
        text.AppendLine("Generated by `ExportRouteShowcaseTests`; regenerate rather than edit.");
        text.AppendLine();
        text.AppendLine("## Open it");
        text.AppendLine();
        text.AppendLine("`open-showcase.sh` opens SoundCharts with every chart, depth, tide, current, warning, area, aid and");
        text.AppendLine("S-129 dataset loaded. The data folder already holds every route in `routes.json`, the RTZ route");
        text.AppendLine("(reopened at start-up) and the Library collection. Copy it first if you want to keep a pristine one.");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine(CultureInfo.InvariantCulture, $"\"{Path.Combine(root, "open-showcase.sh")}\"");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("Select each route in the Routes panel with Check route on; the Clearance tab shows its profile.");
        text.AppendLine("To see a route as *received* (read-only, Native/Planning), select its S-421 dataset in the Library");
        text.AppendLine("and choose **Open as route**. Set the time to 1 March 2026 06:00 UTC for the S-124 warnings.");
        text.AppendLine("The data folder's settings use a 10 m safety contour and safety depth.");
        text.AppendLine();
        text.AppendLine("## Routes");
        text.AppendLine();
        text.AppendLine("| Route | State | Look for |");
        text.AppendLine("|---|---|---|");
        foreach (var showcase in RouteShowcase.Routes)
            text.AppendLine(CultureInfo.InvariantCulture, $"| {showcase.Route.Name} (`{showcase.Route.DatasetName}`) | {showcase.State} | {showcase.LookFor} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| {RouteShowcase.RtzRoute.Name} (`received/{RouteShowcase.RtzRoute.DatasetName}.rtz`) | Received RTZ, read-only | Received badge, Copy to edit, check settings kept locally. |");
        text.AppendLine("| 18 New route with no waypoints | Empty route | Empty states in the panel, map and Check route. |");
        text.AppendLine("| 19 One waypoint, no legs yet | Route with no legs | Check route has nothing to check yet. |");
        text.AppendLine("| 20 Copy to edit of 12 (provenance kept) | Owned copy of a received route | Origin (ZZ00-CHANNEL-1 ed. 1), preserved S-421 content. |");
        return text.ToString();
    }
}
