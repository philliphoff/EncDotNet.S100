#!/usr/bin/env dotnet
// Captures the soundcharts.app screenshots (see ../shot-list.md) by driving
// the SoundCharts viewer over MCP. Public-domain NOAA / USACE data only.
//
//   dotnet build -c Release src/EncDotNet.S100.Viewer      (once)
//   dotnet run site/capture/capture.cs                     (all shots)
//   dotnet run site/capture/capture.cs -- --only H1,F4     (some shots)
//
// Options: --only <ids>, --out <dir>, --viewer <exe>, --ienc <dir>, --chs <dir>, --keep-open, --two-shades,
// --no-clone (run the build in place instead of a cached clone),
// --manifest-only (run the recipes and update the manifest, keeping the existing images).
// Data is downloaded once into site/capture/.cache/ (gitignored).
#:package ModelContextProtocol
#:package SkiaSharp
#:property PublishAot=false

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SkiaSharp;

// Transcript text and console output use invariant (en-US style) numbers.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

var scriptDir = AppContext.GetData("EntryPointFileDirectoryPath") as string
    ?? Path.Combine(Directory.GetCurrentDirectory(), "site", "capture");
var repoRoot = Path.GetFullPath(Path.Combine(scriptDir, "..", ".."));

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var only = Option("--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
var outDir = Path.GetFullPath(Option("--out") ?? Path.Combine(repoRoot, "site", "src", "assets", "shots"));
var viewerExe = Option("--viewer") ?? Path.Combine(repoRoot, "src", "EncDotNet.S100.Viewer", "bin", "Release", "net10.0", "osx-arm64", "SoundCharts");
var chsDir = Option("--chs") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Unencrypted_S100_DatasetsNov2025 (1)", "COMBINED");
var iencDir = Option("--ienc") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SynologyDrive", "Charts", "Inland S-57 ENCs");
var keepOpen = args.Contains("--keep-open");
var manifestOnly = args.Contains("--manifest-only");
ShotManifest.RepoRoot = repoRoot;
Viewer.FourShades = !args.Contains("--two-shades");

var data = new Data(Path.Combine(scriptDir, ".cache"));

// Run a clone of the build from the cache. Other agent sessions clean up with
// name-based kills such as `pkill -f "osx-arm64/SoundCharts"`; a different
// path keeps this run's viewer out of their way.
if (!args.Contains("--no-clone"))
{
    var cloneDir = Path.Combine(scriptDir, ".cache", "viewer");
    if (Directory.Exists(cloneDir)) Directory.Delete(cloneDir, recursive: true);
    Directory.CreateDirectory(Path.GetDirectoryName(cloneDir)!);
    using (var cp = Process.Start("cp", ["-cR", Path.GetDirectoryName(viewerExe)!, cloneDir]))
        await cp.WaitForExitAsync();
    viewerExe = Path.Combine(cloneDir, Path.GetFileName(viewerExe));
}
Directory.CreateDirectory(outDir);

// ── Places ──────────────────────────────────────────────────────────────
// NOAA's rescheme cells (US5SEA**, 0.075° squares) and the band-4 cells
// around them. S-102 tiles share the same names.
string[] seattleEnc = ["US4WA1GJ", "US4WA1GK", .. Grid("US5SEA", "EFG", "IJKL")];
string[] seattleS102 = [.. Grid("102US005SEA", "EFG", "IJKL"), "102US004WA1GJ", "102US004WA1GK"];
// S-101 feature types (as S-57 cells translate) carried into derived S-125 / S-131
// datasets; the names match the target catalogues.
string[] s125Types = ["LateralBuoy", "LateralBeacon", "SpecialPurposeGeneralBuoy", "SpecialPurposeGeneralBeacon",
    "LightAllAround", "LightSectored", "Daymark", "FogSignal", "Landmark", "MooringBuoy", "NavigationLine"];
string[] s131Types = ["Berth", "Dolphin", "MooringBuoy", "HarbourFacility", "DryDock", "FloatingDock", "AnchorageArea"];
// (Derived files are cached in .cache/derived; delete them to re-derive.)
var derivedBox = new Box(47.585, -122.425, 47.640, -122.335);
var elliottBay = new Box(47.540, -122.560, 47.680, -122.320);

var shots = new List<Shot>
{
    // H: hero, the same view in each palette, with the Datasets panel open.
    new("H1", v => Hero(v, "Day")),
    new("H3", v => Hero(v, "Dusk")),
    new("H2", v => Hero(v, "Night")),

    // F1: the same view as layers are added (chart → + depths).
    // F1: the same view at the Chesapeake Bay entrance as layers are added:
    // NOAA chart → + S-102 depths → + S-111 currents (cbofs, latest run).
    new("F1a", v => Layers(v, depths: false, currents: false)),
    new("F1b", v => Layers(v, depths: true, currents: false)),
    new("F1c", v => Layers(v, depths: true, currents: true)),

    // F3: Object Information for the lateral buoy nearest the middle of Elliott Bay.
    new("F3", async v =>
    {
        await v.Reset();
        await v.OpenAll(await data.Enc(seattleEnc));
        var (lat, lon) = await v.Nearest(47.600, -122.370, "LateralBuoy", "BOYLAT");
        await v.Frame(new Box(lat - 0.012, lon - 0.030, lat + 0.012, lon + 0.012));
        await v.Call("pick_features", new() { ["latitude"] = lat, ["longitude"] = lon, ["select"] = true });
        await v.Panel("PickReport");
        return await v.Window();
    }),

    // F4: a route from Colman Dock round West Point to Shilshole Bay Marina.
    new("F4", async v =>
    {
        await v.Reset();
        await v.OpenAll(await data.Enc(seattleEnc));
        await v.Call("create_route", new() { ["name"] = "Colman Dock to Shilshole" });
        foreach (var (lat, lon) in new[]
        {
            (47.6020, -122.3415), (47.6080, -122.3620), (47.6250, -122.4120),
            (47.6520, -122.4420), (47.6700, -122.4330), (47.6795, -122.4105),
        })
            await v.Call("append_waypoint", new() { ["lat"] = lat, ["lon"] = lon });
        await v.Panel("Routes");
        await v.Frame(new Box(47.590, -122.500, 47.690, -122.320));
        return await v.Window();
    }),

    // F2: the currents forecast on the Timeline, Live, with the clock pinned
    // nine hours into the latest cbofs run so the shot is repeatable.
    new("F2", async v =>
    {
        await v.Reset(category: "DisplayBase");
        string[] entrance = ["US4VA1BE", "US4VA1BF"];
        await v.OpenAll(await data.Enc(entrance));
        foreach (var tile in entrance)
            await v.OpenAll([await data.S111("cbofs", tile)]);
        var first = DateTimeOffset.Parse((await v.Call("get_timeline_state"))!["minimum"]!.GetValue<string>());
        await v.Call("set_test_clock", new() { ["now"] = first.AddHours(9).ToString("O"), ["freeze"] = true });
        await v.Call("set_view_time", new() { ["time"] = "now" });
        await v.Call("set_timeline_view", new() { ["preset"] = "this_run" });
        await v.Panel("Timeline");
        await v.Frame(new Box(36.940, -76.460, 37.160, -75.900));
        return await v.Window();
    }),

    // F5: the Library with NOAA's Washington S-102 areas and ENCs, the Seattle
    // items downloaded, over Puget Sound so online and local tiles both show.
    new("F5", async v =>
    {
        await v.Reset();
        var preview = await v.Call("add_library_source", new() { ["knownSourceId"] = "noaa-s102", ["preview"] = true });
        var washington = preview!["choices"]!.AsArray()
            .SelectMany(g => g!["options"]!.AsArray())
            .Select(o => o!["value"]!.GetValue<string>())
            .Where(value => value.StartsWith("Washington/", StringComparison.Ordinal))
            .ToArray();
        await v.Call("add_library_source", new() { ["knownSourceId"] = "noaa-s102", ["choices"] = washington });
        await v.Call("add_library_source", new() { ["knownSourceId"] = "noaa-enc", ["choices"] = new[] { "WA" } });
        await v.Call("await_library_idle", new() { ["timeoutMs"] = 120000 });
        var seattle = new Dictionary<string, object?> { ["south"] = 47.55, ["west"] = -122.45, ["north"] = 47.65, ["east"] = -122.32 };
        await v.Call("library_action", new(seattle) { ["action"] = "download", ["spec"] = "S-102", ["maxBytes"] = 100_000_000L });
        await v.Call("library_action", new(seattle) { ["action"] = "download", ["spec"] = "S-57", ["maxBytes"] = 50_000_000L });
        await v.Call("await_library_idle", new() { ["timeoutMs"] = 300000 });
        await v.WaitForLoads();
        await v.Panel("Library");
        await v.Frame(new Box(47.40, -122.75, 47.80, -122.10));
        return await v.Window();
    }),

    // F8: the Validation tab for whichever candidate has the most located
    // findings, over the NOAA chart at the Chesapeake Bay entrance: the S-125 AtoN
    // fixture (synthetic) and NOAA's own S-111 currents (a few 360° directions,
    // which the spec's [0, 360) range rejects). NOAA's ENCs now validate clean.
    new("F8", async v =>
    {
        await v.Reset();
        await v.OpenAll(await data.Enc(["US4VA1BF"]));
        var candidates = new List<(string Id, Box Bounds)>();
        foreach (var file in new[] { Fixture("S125/aton_chesapeake.gml"), await data.S111("cbofs", "US4VA1BF") })
        {
            v.ClearOpened();
            foreach (var id in await v.OpenAll([file])) candidates.Add((id, v.Opened!));
        }
        await v.Panel("Datasets");
        (string Id, Box Bounds, int Located, int Total)? best = null;
        foreach (var (id, bounds) in candidates)
        {
            var summary = (await v.Call("select_dataset", new() { ["datasetId"] = id, ["tab"] = "validation" }))?["validation"];
            var located = summary?["located"]?.GetValue<int>() ?? 0;
            var total = summary?["total"]?.GetValue<int>() ?? 0;
            Console.WriteLine($"  {id}: {summary?["state"]} {total} findings, {located} located");
            if (best is null || (located, total).CompareTo((best.Value.Located, best.Value.Total)) > 0) best = (id, bounds, located, total);
        }
        // Keep the map readable: close the other candidates, then show the winner.
        foreach (var (id, _) in candidates.Where(c => c.Id != best!.Value.Id))
            await v.Call("close_dataset", new() { ["id"] = id });
        await v.Call("select_dataset", new() { ["datasetId"] = best!.Value.Id, ["tab"] = "validation" });
        await v.Frame(best.Value.Bounds);
        return await v.Window();
    }),

    // F9: the ECDIS display controls beside Shilshole Bay and the approach to the
    // Ballard Locks, where the safety contour and four-shade depths show.
    new("F9", async v =>
    {
        await v.Reset();
        await v.OpenAll(await data.Enc(seattleEnc));
        await v.OpenAll(await data.S102(seattleS102));
        await v.Panel("EcdisDisplay");
        await v.Frame(new Box(47.655, -122.440, 47.700, -122.370));
        return await v.Window();
    }),

    // D2: an agent planning and checking a route over MCP. Every call and result
    // in the transcript is real; the frames are window captures after each step,
    // encoded to site/public/clips/D2.{mp4,webm}. The returned PNG is the poster.
    new("D2", v => AgentClip(v)),

    // F7: one harbour crop in each palette (window captures: render_to_image ignores the palette).
    new("F7-day", v => Palette(v, "Day")),
    new("F7-dusk", v => Palette(v, "Dusk")),
    new("F7-night", v => Palette(v, "Night")),

    // P: square product tiles, map only.
    // P01: real S-101 from the Canadian Hydrographic Service's S-100 sample
    // package (Nov 2025): Quebec City harbour at the cell's optimum scale. CHS
    // licence: non-commercial use with the CHS notice (in the page footer); the
    // data itself is never committed, so it's read from --chs (a local copy).
    new("P01", async v =>
    {
        var cell = Directory.Exists(chsDir)
            ? Directory.GetFiles(chsDir, "101CA00P468N0712W.000", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (cell is null) throw new SkipShot($"CHS sample package not found under {chsDir} (pass --chs)");
        await v.Reset();
        await v.OpenAll([cell]);
        await v.Call("set_viewport", new() { ["centerLat"] = 46.835, ["centerLon"] = -71.170, ["scaleDenominator"] = 12000 });
        return await v.Map(1200, 1200);
    }),
    new("P02", async v =>
    {
        await v.Reset();
        await v.OpenAll(await data.Enc(seattleEnc));
        await v.Frame(new Box(47.590, -122.365, 47.615, -122.330));
        return await v.Map(1200, 1200);
    }),
    new("P03", async v =>
    {
        // S-102 over the NOAA chart at Display Base: land and coastline come from
        // the chart (the offline Natural Earth land is far too coarse at harbour
        // scale), and four-shade depth zones give the spec's fullest gradation.
        await v.Reset(category: "DisplayBase");
        await v.OpenAll(await data.Enc(seattleEnc));
        await v.OpenAll(await data.S102(seattleS102));
        await v.Frame(new Box(47.520, -122.560, 47.660, -122.330));
        return await v.Map(1200, 1200);
    }),
    // P04: NOAA's S-104 water-level pilot at Charleston (repo fixture, Dec 2025 run).
    // The gridded tiles load hidden, and the run is in the past, so switch them
    // on and move the Timeline to their data. The NOAA chart sits underneath at
    // Display Base; S-98 puts the S-104 band over its fills, under its line work.
    new("P04", async v =>
    {
        await v.Reset(category: "DisplayBase");
        await v.OpenAll(await data.Enc(["US4SC1BO", "US4SC1BP", "US4SC1CO", "US4SC1CP"]));
        var levels = await v.OpenAll(Directory.GetFiles(Fixture("S104"), "104US004SC1*.h5"));
        foreach (var id in levels)
            await v.Call("set_dataset_state", new() { ["datasetId"] = id, ["visible"] = true });
        var first = DateTimeOffset.Parse((await v.Call("get_timeline_state"))!["minimum"]!.GetValue<string>());
        await v.Call("set_view_time", new() { ["time"] = first.AddHours(6).ToString("O"), ["snap"] = "nearest" });
        await v.Frame(new Box(32.640, -80.000, 32.800, -79.800));
        return await v.Map(1200, 1200);
    }),
    new("P05", v => Currents(v, data.S111("cbofs", "US4VA1BF"))),
    // P06, P08–P10: repo fixtures for the GML products, framed on their data.
    new("P06", v => Tile(v, [Fixture("S124/navwarn_mixed.gml")])),
    // P07: S-125 aids to navigation derived from NOAA's Elliott Bay ENCs (public
    // domain), drawn over the chart.
    // S-125 now portrays AtoNs with the S-101 rules (#750), which sit in the
    // Standard display category, so this tile can't use Display Base.
    new("P07", async v => await DerivedTile(v, "S-125", "S125", s125Types, category: "Standard")),
    // P08: US National Weather Service sea-ice analysis for Alaska (public
    // domain), via the BSH/BSIS S-411 Ice Portal, framed on the Beaufort and
    // Chukchi ice edge.
    new("P08", async v =>
    {
        await v.Reset();
        await v.OpenAll([await data.S411Latest("S411_NWS_full_")]);
        // NWS publishes Alaska in a continuous longitude frame across the
        // antimeridian (≈175°E–225°E), so frame it in that frame: 190–220 = 170°W–140°W.
        await v.Call("set_viewport", new() { ["centerLat"] = 72.0, ["centerLon"] = 180.0, ["scaleDenominator"] = 10_000_000 });
        return await v.Map(1200, 1200);
    }),
    new("P09", v => Tile(v, [Fixture("S421/RTE-TEST-GFULL.s421.gml")])),
    new("P10", v => Tile(v, [Fixture("S129/12900MCTDS200TS.gml")])),
    // P12: S-131 harbour infrastructure derived from the same NOAA cells.
    new("P12", async v => await DerivedTile(v, "S-131", "S131", s131Types)),
    new("P11", async v =>
    {
        // USACE inland ENC: the Upper Mississippi and Missouri cells at St. Louis (local corpus).
        var cells = new[] { "U37UM155", "U37MO000" }
            .Select(c => Path.Combine(iencDir, c, $"{c}.000"))
            .Where(File.Exists)
            .ToArray();
        if (cells.Length == 0) throw new SkipShot($"no USACE IENC cells under {iencDir} (pass --ienc)");
        await v.Reset();
        await v.OpenAll(cells);
        await v.Frame(new Box(38.605, -90.205, 38.645, -90.165));
        return await v.Map(1200, 1200);
    }),
};

// ── Run ────────────────────────────────────────────────────────────────
var selected = shots.Where(s => only is null || only.Contains(s.Id)).ToList();
if (selected.Count == 0)
{
    Console.Error.WriteLine($"No shots match --only. Known: {string.Join(", ", shots.Select(s => s.Id))}");
    return 1;
}

var viewer = await Viewer.StartAsync(viewerExe, keepOpen);
var failures = 0;
try
{
    foreach (var shot in selected)
    {
        for (var attempt = 1; ; attempt++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                viewer.BeginShot();
                var png = await shot.Capture(viewer);
                var path = Path.Combine(outDir, $"{shot.Id}.png");
                var keepImage = manifestOnly && File.Exists(path);
                if (keepImage)
                {
                    Console.WriteLine($"= {shot.Id,-9} manifest only (image kept)");
                }
                else
                {
                    await File.WriteAllBytesAsync(path, png);
                    Console.WriteLine($"✓ {shot.Id,-9} {png.Length / 1024,6} KB  {sw.Elapsed.TotalSeconds,5:0.0}s  {Path.GetRelativePath(repoRoot, path)}");
                }
                await ShotManifest.RecordAsync(Path.Combine(outDir, "manifest.json"), shot.Id, viewer, path, repoRoot, recapturedImage: !keepImage);
            }
            catch (SkipShot e)
            {
                Console.WriteLine($"- {shot.Id,-9} skipped: {e.Message}");
            }
            catch (Exception e) when (attempt < 4 && viewer.Died())
            {
                // Another process can take the viewer down (e.g. a name-based kill
                // from a parallel session); start a fresh one and retry the shot.
                Console.WriteLine($"  viewer died during {shot.Id} ({e.Message}); restarting");
                await viewer.DisposeAsync();
                viewer = await Viewer.StartAsync(viewerExe, keepOpen);
                continue;
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine($"✗ {shot.Id,-9} {e.Message}");
            }
            break;
        }
    }
}
finally
{
    await viewer.DisposeAsync();
}
return failures == 0 ? 0 : 1;

// ── Shot recipes ───────────────────────────────────────────────────────
// ── D2: agent clip ──────────────────────────────────────────────────────
async Task<byte[]> AgentClip(Viewer v)
{
    await v.Reset();
    await v.OpenAll(await data.Enc(seattleEnc));
    await v.OpenAll(await data.S102(seattleS102));
    await v.Panel("Routes");
    await v.Frame(new Box(47.590, -122.500, 47.690, -122.320));

    var clip = new AgentClipBuilder();
    clip.Say("user", "Plan a route from Colman Dock to Shilshole Bay Marina, and check the depths along it.");
    await clip.Frame(v, 2.5);

    const string name = "Colman Dock to Shilshole";
    var created = await v.Call("create_route", new() { ["name"] = name });
    clip.Call($"create_route {{ name: \"{name}\" }}", $"route {created!["routeId"]!.GetValue<string>()[..8]}…, 0 waypoints");
    await clip.Frame(v, 1.2);

    (double Lat, double Lon)[] waypoints =
    [
        (47.6020, -122.3415), (47.6080, -122.3620), (47.6250, -122.4120),
        (47.6520, -122.4420), (47.6700, -122.4330), (47.6795, -122.4105),
    ];
    foreach (var (lat, lon) in waypoints)
    {
        var route = await v.Call("append_waypoint", new() { ["lat"] = lat, ["lon"] = lon });
        var count = route!["waypoints"]!.AsArray().Count;
        var total = route["totalDistanceNm"]!.GetValue<double>();
        clip.Call($"append_waypoint {{ lat: {lat:0.0000}, lon: {lon:0.0000} }}", $"{count} waypoint{(count == 1 ? "" : "s")} · {total:0.0} NM");
        await clip.Frame(v, 0.7);
    }

    // Check depths along the track, densified (~150 m) so the S-102 sampling
    // follows the legs, not just the turns. Samples within ~300 m of either end
    // (the berths) are left out of the least depth.
    async Task<(double Depth, double Lat, double Lon, int Count, int Points)> LeastDepth((double Lat, double Lon)[] route)
    {
        var track = new JsonArray();
        for (var i = 0; i < route.Length - 1; i++)
        {
            var (a, b) = (route[i], route[i + 1]);
            var metres = Math.Sqrt(Math.Pow((b.Lat - a.Lat) * 111_320, 2) + Math.Pow((b.Lon - a.Lon) * 111_320 * Math.Cos(a.Lat * Math.PI / 180), 2));
            var steps = Math.Max(1, (int)(metres / 150));
            for (var k = 0; k < steps; k++)
                track.Add(new JsonArray(a.Lat + (b.Lat - a.Lat) * k / steps, a.Lon + (b.Lon - a.Lon) * k / steps));
        }
        track.Add(new JsonArray(route[^1].Lat, route[^1].Lon));
        var points = track.Count;
        // The tool takes the polyline as a JSON string: {"vertices":[[lat,lon],...]}.
        var along = await v.Call("sample_coverage_along", new() { ["spec"] = "S-102", ["polyline"] = new JsonObject { ["vertices"] = track }.ToJsonString() });
        var samples = along!["samples"]!.AsArray();
        var found = samples
            .Select((sample, i) => (Depth: sample?["result"]?["value"]?["depthMeters"]?.GetValue<double>(), Index: i, Sample: sample))
            .Where(x => x.Depth is not null).ToList();
        var least = found.Where(x => x.Index >= 2 && x.Index < samples.Count - 2).MinBy(x => x.Depth!.Value);
        return (least.Depth!.Value, least.Sample!["latitude"]!.GetValue<double>(), least.Sample!["longitude"]!.GetValue<double>(), found.Count, points);
    }

    var first = await LeastDepth(waypoints);
    clip.Call($"sample_coverage_along {{ spec: \"S-102\", {first.Points} points }}",
        $"{first.Count} depths · least {first.Depth:0.0} m at {first.Lat:0.000}, {first.Lon:0.000}");
    await clip.Frame(v, 1.6);

    // The first track clips the West Point shoal; move waypoints 4 and 5 into deeper water.
    (double Lat, double Lon)[] offshore = [(47.6520, -122.4500), (47.6700, -122.4420)];
    clip.Say("assistant", $"That leg crosses the shoal off West Point ({first.Depth:0.0} m). Moving waypoints 4 and 5 into deeper water.");
    for (var i = 0; i < offshore.Length; i++)
    {
        var (lat, lon) = offshore[i];
        await v.Call("move_waypoint", new() { ["index"] = 3 + i, ["lat"] = lat, ["lon"] = lon });
        waypoints[3 + i] = (lat, lon);
        clip.Call($"move_waypoint {{ index: {3 + i}, lat: {lat:0.0000}, lon: {lon:0.0000} }}", "moved");
        await clip.Frame(v, 1.0);
    }

    var second = await LeastDepth(waypoints);
    clip.Call($"sample_coverage_along {{ spec: \"S-102\", {second.Points} points }}", $"{second.Count} depths · least {second.Depth:0.0} m");
    await clip.Frame(v, 1.4);

    var length = (await v.Call("get_route"))!["totalDistanceNm"]!.GetValue<double>();
    clip.Say("assistant", $"Done: {waypoints.Length} waypoints, {length:0.0} NM. Away from the berths, the least depth along the track is now {second.Depth:0.0} m (NOAA S-102).");
    var poster = await clip.Frame(v, 3.5);

    var clipsDir = Path.Combine(repoRoot, "site", "public", "clips");
    if (!manifestOnly) await clip.EncodeAsync(clipsDir, "D2");
    return poster;
}

// A product tile from a dataset derived from the NOAA Elliott Bay cells.
async Task<byte[]> DerivedTile(Viewer v, string spec, string prefix, string[] types, string category = "DisplayBase", bool showChart = true)
{
    await v.Reset(category: category);
    // The ENCs are needed to derive the data; hide them for an S-xxx-only tile.
    var cells = await v.OpenAll(await data.Enc(seattleEnc));
    var path = Path.Combine(scriptDir, ".cache", "derived", $"{prefix}_ElliottBay.gml");
    if (!File.Exists(path))
    {
        Console.WriteLine($"  deriving {spec} from the NOAA cells");
        await GmlDeriver.WriteAsync(v, spec, prefix, types, derivedBox, path);
    }
    if (!showChart)
        foreach (var cell in cells)
            await v.Call("set_dataset_state", new() { ["datasetId"] = cell, ["visible"] = false });
    await v.OpenAll([path]);
    await v.Frame(derivedBox);
    return await v.Map(1200, 1200);
}

// A product tile: open the files (optionally over NOAA chart cells at Display
// Base) and frame their combined bounds with a little margin.
async Task<byte[]> Tile(Viewer v, IEnumerable<string> files, string[]? enc = null)
{
    await v.Reset(category: enc is null ? "Standard" : "DisplayBase");
    if (enc is not null) await v.OpenAll(await data.Enc(enc));
    v.ClearOpened();
    await v.OpenAll(files);
    var b = v.Opened ?? throw new InvalidOperationException("nothing opened");
    double padLat = Math.Max((b.North - b.South) * 0.08, 0.002), padLon = Math.Max((b.East - b.West) * 0.08, 0.002);
    await v.Frame(new Box(b.South - padLat, b.West - padLon, b.North + padLat, b.East + padLon));
    return await v.Map(1200, 1200);
}

string Fixture(string relative) => Path.Combine(repoRoot, "tests", "datasets", relative);

async Task<byte[]> Hero(Viewer v, string palette)
{
    await v.Reset(palette);
    await v.OpenAll(await data.Enc(seattleEnc));
    await v.OpenAll(await data.S102(seattleS102));
    await v.Frame(elliottBay);
    return await v.Window();
}

async Task<byte[]> Layers(Viewer v, bool depths, bool currents)
{
    string[] tiles = ["US4VA1BE", "US4VA1BF"];
    await v.Reset();
    await v.OpenAll(await data.Enc(tiles));
    if (depths) await v.OpenAll(await data.S102(["102US004VA1BE", "102US004VA1BF"]));
    if (currents)
        foreach (var tile in tiles)
            await v.OpenAll([await data.S111("cbofs", tile)]);
    await v.Frame(new Box(36.935, -76.120, 37.075, -75.960));
    return await v.Window();
}

// Chesapeake Bay entrance: the model's strongest currents, over the matching
// chart at Display Base so the arrows lead.
async Task<byte[]> Currents(Viewer v, Task<string> currents)
{
    await v.Reset(category: "DisplayBase");
    await v.OpenAll(await data.Enc(["US4VA1BF"]));
    await v.OpenAll([await currents]);
    await v.Frame(new Box(36.935, -76.120, 37.075, -75.960));
    return await v.Map(1200, 1200);
}

async Task<byte[]> Palette(Viewer v, string palette)
{
    await v.Reset(palette);
    await v.OpenAll(await data.Enc(seattleEnc));
    await v.Frame(new Box(47.596, -122.360, 47.616, -122.330));
    return await v.Window();
}

static IEnumerable<string> Grid(string prefix, string rows, string cols) =>
    from r in rows from c in cols select $"{prefix}{r}{c}";

// ── Types ──────────────────────────────────────────────────────────────
record Box(double South, double West, double North, double East);

record Shot(string Id, Func<Viewer, Task<byte[]>> Capture);

sealed class SkipShot(string message) : Exception(message);

/// <summary>Downloads public NOAA data on first use into a local cache.</summary>
sealed class Data(string cacheDir)
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };
    private Dictionary<string, string>? _s102Index;

    /// <summary>NOAA ENC cells (S-57), from charts.noaa.gov. Returns the .000 paths.</summary>
    public async Task<string[]> Enc(IEnumerable<string> cells)
    {
        var paths = new List<string>();
        foreach (var cell in cells)
        {
            var dir = Path.Combine(cacheDir, "enc", cell);
            var cellFile = Path.Combine(dir, "ENC_ROOT", cell, $"{cell}.000");
            if (!File.Exists(cellFile))
            {
                Console.WriteLine($"  downloading ENC {cell}");
                var zip = await Http.GetByteArrayAsync($"https://charts.noaa.gov/ENCs/{cell}.zip");
                Directory.CreateDirectory(dir);
                ZipFile.ExtractToDirectory(new MemoryStream(zip), dir, overwriteFiles: true);
            }
            paths.Add(cellFile);
        }
        return [.. paths];
    }

    /// <summary>NOAA S-102 tiles by cell name (the edition suffix is looked up in the catalogue).</summary>
    public async Task<string[]> S102(IEnumerable<string> cells)
    {
        _s102Index ??= await LoadS102IndexAsync();
        var paths = new List<string>();
        foreach (var cell in cells)
        {
            if (!_s102Index.TryGetValue(cell, out var rel)) throw new InvalidOperationException($"S-102 tile {cell} is not in NOAA's catalogue");
            paths.Add(await DownloadAsync($"https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/{rel}", Path.Combine("s102", Path.GetFileName(rel))));
        }
        return [.. paths];
    }

    /// <summary>The latest run of one NOAA S-111 model's tile (e.g. cbofs, US4VA1DD).</summary>
    public async Task<string> S111(string model, string tile)
    {
        var root = $"https://noaa-s111-pds.s3.amazonaws.com/ed1.0.1/model_forecast_guidance/{model}/";
        var catalogue = await Http.GetStringAsync(root + "CATALOG.XML");
        // Each entry lists fileName then filePath (directory) for the run.
        var match = Regex.Matches(catalogue, @"fileName>\s*([^<]*?)\s*</[^>]*fileName>\s*<[^>]*filePath>\s*([^<]*?)\s*</")
            .FirstOrDefault(m => m.Groups[1].Value.EndsWith($"_{tile}.h5", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{model} has no tile {tile} in its latest run");
        var fileName = match.Groups[1].Value;
        return await DownloadAsync(root + match.Groups[2].Value.TrimEnd('/') + "/" + fileName, Path.Combine("s111", model, fileName));
    }

    /// <summary>
    /// The newest S-411 chart whose name starts with <paramref name="prefix"/> from the
    /// BSIS Ice Portal (https://www.bsis-ice.de/IcePortal/), or the newest cached one.
    /// </summary>
    public async Task<string> S411Latest(string prefix)
    {
        var dir = Path.Combine(cacheDir, "s411");
        string? name = null;
        try
        {
            var page = await Http.GetStringAsync("https://www.bsis-ice.de/IcePortal/");
            name = Regex.Matches(page, $@"S411/({Regex.Escape(prefix)}[\w-]+)\.zip").Select(m => m.Groups[1].Value).OrderBy(n => n).LastOrDefault();
        }
        catch (HttpRequestException) { }
        name ??= Directory.Exists(dir)
            ? Directory.GetDirectories(dir, prefix + "*").Select(Path.GetFileName).OrderBy(n => n).LastOrDefault()
            : null;
        if (name is null) throw new InvalidOperationException($"no {prefix}* chart on the BSIS Ice Portal or in the cache");

        var gml = Path.Combine(dir, name, name, "data", name + ".gml");
        if (!File.Exists(gml))
        {
            Console.WriteLine($"  downloading {name}.zip");
            var zip = await Http.GetByteArrayAsync($"https://www.bsis-ice.de/IcePortal/S411/{name}.zip");
            ZipFile.ExtractToDirectory(new MemoryStream(zip), Path.Combine(dir, name), overwriteFiles: true);
        }
        return gml;
    }

    /// <summary>The latest run of one NOAA S-111 model's whole-domain file (dcf2 grid or dcf3 mesh).</summary>
    public async Task<string> S111Regional(string model, string encoding)
    {
        var root = $"https://noaa-s111-pds.s3.amazonaws.com/ed1.0.1/model_forecast_guidance/{model}/";
        var catalogue = await Http.GetStringAsync(root + "CATALOG.XML");
        // Tiles are named 111US00_<MODEL>_<run>_<tile>.h5 under <run dir>/dcf2/tiles/;
        // the regional file drops the tile suffix and sits under <run dir>/<encoding>/regional/.
        var tile = Regex.Match(catalogue, @"fileName>\s*(111US00_\w+?_\d{8}T\d{2}Z)_\w+\.h5\s*</[^>]*fileName>\s*<[^>]*filePath>\s*(\d{4}/\d{2}/\d{2}/\d{2})/");
        if (!tile.Success) throw new InvalidOperationException($"{model}'s catalogue lists no run");
        var fileName = tile.Groups[1].Value + ".h5";
        var url = $"{root}{tile.Groups[2].Value}/{encoding}/regional/{fileName}";
        return await DownloadAsync(url, Path.Combine("s111", model, encoding, fileName));
    }

    private async Task<Dictionary<string, string>> LoadS102IndexAsync()
    {
        Console.WriteLine("  reading NOAA S-102 catalogue");
        var xml = await Http.GetStringAsync("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML");
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(xml, @"file:\.\./([^<\s]*?/(102US\w{8})\w*\.h5)"))
            index[m.Groups[2].Value] = m.Groups[1].Value;
        return index;
    }

    private async Task<string> DownloadAsync(string url, string relative)
    {
        var path = Path.Combine(cacheDir, relative);
        if (File.Exists(path)) return path;
        Console.WriteLine($"  downloading {Path.GetFileName(path)}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = await Http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(path + ".part", bytes);
        File.Move(path + ".part", path, overwrite: true);
        return path;
    }
}

/// <summary>A SoundCharts process in a throwaway data folder, driven over MCP.</summary>
sealed class Viewer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly McpClient _client;
    private readonly string _dataDir;
    private readonly bool _keepOpen;

    private Viewer(Process process, McpClient client, string dataDir, bool keepOpen) =>
        (_process, _client, _dataDir, _keepOpen) = (process, client, dataDir, keepOpen);

    /// <summary>Whether to use the mariner's four-shade depth zones (default; --two-shades turns them off).</summary>
    public static bool FourShades { get; set; }

    public static async Task<Viewer> StartAsync(string exe, bool keepOpen)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("Build the viewer first: dotnet build -c Release src/EncDotNet.S100.Viewer", exe);

        var dataDir = Directory.CreateTempSubdirectory("soundcharts-capture-").FullName;
        var settings = Path.Combine(dataDir, "settings.json");
        // Dark chrome, no update prompt or status bar, docks closed until a shot opens one.
        // S-101 viewing groups 90020/90021 (the INFORM01 "additional information"
        // markers) are hidden, as a mariner would, so harbour views stay readable;
        // the out-of-scale extent outlines are off for the same reason, and a
        // small-craft 10 m safety contour keeps isolated-danger marks to real hazards.
        await File.WriteAllTextAsync(settings, $$"""
            {
              "ChromeTheme": "Dark", "UpdateCheckEnabled": false, "IsStatusBarVisible": false,
              "IsLeftDockOpen": false, "IsRightDockOpen": false, "IsBottomDockOpen": false,
              "EcdisHiddenViewingGroups": { "S-101": "90020,90021", "S-57": "90020,90021" },
              "ShowOutOfScaleExtentIndicators": false,
              "SafetyContour": 10, "SafetyDepth": 10,
              "FourShades": {{FourShades.ToString().ToLowerInvariant()}}
            }
            """);
        var portFile = Path.Combine(dataDir, "mcp.url");
        var process = Process.Start(new ProcessStartInfo(exe)
        {
            ArgumentList =
            {
                "--data-dir", dataDir, "--settings", settings, "--mcp-test-hooks", "--mcp-port-file", portFile,
                "--basemap", "Offline", "--crash-log", Path.Combine(dataDir, "crash.log"),
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Site captures use en-US number and date formats whatever this machine's locale.
            Environment = { ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8" },
        }) ?? throw new InvalidOperationException("Could not start the viewer.");
        // Keep the viewer's console output next to its crash log for diagnosis.
        var log = TextWriter.Synchronized(new StreamWriter(Path.Combine(dataDir, "viewer.log")) { AutoFlush = true });
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(portFile) || new FileInfo(portFile).Length == 0)
        {
            if (process.HasExited) throw new InvalidOperationException($"The viewer exited during startup (code {process.ExitCode}).");
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The viewer did not start its MCP server within 60 s.");
            await Task.Delay(250);
        }
        var endpoint = new Uri((await File.ReadAllTextAsync(portFile)).Trim());
        var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "soundcharts-capture",
        }));
        Console.WriteLine($"Viewer pid {process.Id}, MCP {endpoint}");
        var viewer = new Viewer(process, client, dataDir, keepOpen);

        // The MCP server starts before the map is attached; wait until the
        // presentation tools stop answering host_not_ready.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await viewer.Call("set_palette", new() { ["palette"] = "Day" });
                return viewer;
            }
            catch (InvalidOperationException e) when (e.Message.Contains("host_not_ready") && attempt < 120)
            {
                await Task.Delay(500);
            }
        }
    }

    /// <summary>Whether the viewer process has exited (waiting briefly for a kill to land).</summary>
    public bool Died() => _process.WaitForExit(TimeSpan.FromSeconds(3));

    // Tools whose calls change what a shot shows; recorded per shot for the manifest.
    private static readonly HashSet<string> Recorded =
    [
        "open_dataset", "close_dataset", "set_viewport", "set_palette", "set_display_category", "set_panel",
        "set_dataset_state", "set_test_clock", "set_view_time", "set_timeline_view", "select_dataset", "pick_features",
        "create_route", "append_waypoint", "move_waypoint", "add_library_source", "library_action", "sample_coverage_along",
    ];
    private bool _quiet;

    /// <summary>The recorded calls of the current shot, in order (see <see cref="BeginShot"/>).</summary>
    public JsonArray Steps { get; private set; } = [];

    /// <summary>Source file of every dataset opened by path, by dataset id.</summary>
    public Dictionary<string, string> Paths { get; } = new(StringComparer.Ordinal);

    /// <summary>How the current shot's image was taken.</summary>
    public string? CaptureMode { get; private set; }

    public void BeginShot()
    {
        Steps = [];
        CaptureMode = null;
    }

    public async Task<JsonNode?> Call(string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await _client.CallToolAsync(tool, arguments ?? []);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (result.IsError == true) throw new InvalidOperationException($"{tool}: {text ?? "failed"}");
        if (!_quiet && Recorded.Contains(tool))
            Steps.Add(new JsonObject { ["tool"] = tool, ["args"] = ShotManifest.Arguments(arguments) });
        return text is null ? null : JsonNode.Parse(text);
    }

    /// <summary>Back to a clean slate: no data, no panels, the given palette, no notifications.</summary>
    public async Task Reset(string palette = "Day", string category = "Standard")
    {
        Steps.Add(new JsonObject { ["tool"] = "reset", ["args"] = new JsonObject { ["palette"] = palette, ["displayCategory"] = category } });
        _quiet = true;
        try { await ResetCore(palette, category); }
        finally { _quiet = false; }
    }

    private async Task ResetCore(string palette, string category)
    {
        Opened = null;
        await Call("close_all_datasets");
        await Call("set_palette", new() { ["palette"] = palette });
        await Call("set_display_category", new() { ["displayCategory"] = category });
        var panels = (await Call("list_panels"))?["panels"]?.AsArray() ?? [];
        foreach (var p in panels.Where(p => p?["showing"]?.GetValue<bool>() == true))
            await Call("set_panel", new() { ["panel"] = p!["id"]!.GetValue<string>(), ["visible"] = false });
        foreach (var r in (await Call("list_routes"))?["routes"]?.AsArray() ?? [])
            await Call("delete_route", new() { ["routeId"] = r!["routeId"]!.GetValue<string>() });
        foreach (var collection in (await Call("list_library_sources"))?["collections"]?.AsArray() ?? [])
            await Call("remove_library_source", new() { ["id"] = collection!["id"]!.GetValue<string>(), ["confirm"] = true });
        await Call("set_test_clock", new() { ["reset"] = true });
    }

    /// <summary>Forgets the bounds collected by <see cref="OpenAll"/>.</summary>
    public void ClearOpened() => Opened = null;

    /// <summary>Union of the bounds of every dataset opened since the last <see cref="Reset"/>.</summary>
    public Box? Opened { get; private set; }

    public async Task<IReadOnlyList<string>> OpenAll(IEnumerable<string> paths, bool tolerateFailures = false)
    {
        var ids = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var result = await Call("open_dataset", new() { ["path"] = path });
                foreach (var d in result?["datasets"]?.AsArray() ?? [])
                {
                    ids.Add(d!["id"]!.GetValue<string>());
                    Paths[d["id"]!.GetValue<string>()] = path;
                    var b = new Box(d["southLatitude"]!.GetValue<double>(), d["westLongitude"]!.GetValue<double>(),
                        d["northLatitude"]!.GetValue<double>(), d["eastLongitude"]!.GetValue<double>());
                    Opened = Opened is { } o
                        ? new Box(Math.Min(o.South, b.South), Math.Min(o.West, b.West), Math.Max(o.North, b.North), Math.Max(o.East, b.East))
                        : b;
                }
            }
            catch (InvalidOperationException) when (tolerateFailures)
            {
            }
        }
        return ids;
    }

    /// <summary>Waits until every downloaded Library item is open (downloads load after they finish).</summary>
    public async Task WaitForLoads()
    {
        for (var i = 0; i < 120; i++)
        {
            var waiting = (await Call("query_library_items", new() { ["states"] = new[] { "local" } }))?["total"]?.GetValue<int>() ?? 0;
            if (waiting == 0) return;
            await Task.Delay(2500);
        }
        throw new TimeoutException("Library downloads did not finish loading within 5 minutes.");
    }

    public Task Panel(string id) => Call("set_panel", new() { ["panel"] = id, ["visible"] = true });

    /// <summary>The nearest point of the closest feature of any of the given types.</summary>
    public async Task<(double Lat, double Lon)> Nearest(double lat, double lon, params string[] featureTypes)
    {
        foreach (var type in featureTypes)
        {
            var hit = (await Call("nearest_features", new() { ["latitude"] = lat, ["longitude"] = lon, ["featureType"] = type, ["limit"] = 1 }))
                ?["features"]?.AsArray().FirstOrDefault();
            if (hit is not null)
                return (hit["nearestLatitude"]!.GetValue<double>(), hit["nearestLongitude"]!.GetValue<double>());
        }
        throw new InvalidOperationException($"no {string.Join("/", featureTypes)} near {lat}, {lon}");
    }

    public async Task Frame(Box box)
    {
        await Call("set_viewport", new() { ["south"] = box.South, ["west"] = box.West, ["north"] = box.North, ["east"] = box.East });
        await Settle();
    }

    public async Task Settle()
    {
        await Call("await_render_idle", new() { ["quietPeriodMs"] = 1500, ["timeoutMs"] = 120000 });
        await Call("dismiss_notification");
    }

    public async Task<byte[]> Window()
    {
        await Settle();
        CaptureMode = "window (capture_app_screenshot, scale 2)";
        return await Image("capture_app_screenshot", new() { ["scale"] = 2.0 });
    }

    public async Task<byte[]> Map(int width, int height)
    {
        await Settle();
        // Logical size at 2× density, so symbols and text keep their on-screen size.
        CaptureMode = $"map (render_to_image {width / 2}×{height / 2}, pixel density 2)";
        var png = await Image("render_to_image", new() { ["width"] = width / 2, ["height"] = height / 2, ["pixelDensity"] = 2.0 });
        // Don't overwrite a stand-in with an empty map. (Draw-call counts can't tell:
        // a fully drawn tiled map composites to a single call.)
        using (var bitmap = SKBitmap.Decode(png))
        {
            var colours = new HashSet<SKColor>();
            for (var y = 0; y < bitmap.Height; y += bitmap.Height / 40)
                for (var x = 0; x < bitmap.Width; x += bitmap.Width / 40)
                    colours.Add(bitmap.GetPixel(x, y));
            if (colours.Count < 3) throw new SkipShot("the map is empty (nothing drew)");
        }
        return png;
    }

    private async Task<byte[]> Image(string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await _client.CallToolAsync(tool, arguments ?? []);
        if (result.IsError == true)
            throw new InvalidOperationException($"{tool}: {result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "failed"}");
        var image = result.Content.OfType<ImageContentBlock>().FirstOrDefault()
            ?? throw new InvalidOperationException($"{tool} returned no image");
        var bytes = image.Data.ToArray();
        // The block carries base64 text; tolerate SDKs that hand back raw bytes.
        return bytes is [0x89, (byte)'P', (byte)'N', (byte)'G', ..] ? bytes : Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(bytes));
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        if (_keepOpen)
        {
            Console.WriteLine($"Viewer left running (pid {_process.Id}, data {_dataDir}).");
            return;
        }
        if (_process.HasExited)
        {
            // It died on its own: keep the logs.
            Console.WriteLine($"Viewer exited unexpectedly (code {_process.ExitCode}); logs kept in {_dataDir}.");
            return;
        }
        _process.Kill();
        await _process.WaitForExitAsync();
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Builds the D2 clip: a transcript panel beside window captures, encoded with ffmpeg.</summary>
sealed class AgentClipBuilder
{
    private const int Width = 1920, Height = 1080, PanelWidth = 560, Margin = 40;
    private readonly List<(string Kind, string Text, string? Detail)> _entries = [];
    private readonly List<(byte[] Png, double Seconds)> _frames = [];
    private static readonly SKTypeface Sans = SKTypeface.FromFile("/System/Library/Fonts/SFNS.ttf") ?? SKTypeface.Default;
    private static readonly SKTypeface Mono = SKTypeface.FromFile("/System/Library/Fonts/SFNSMono.ttf") ?? SKTypeface.Default;

    public void Say(string role, string text) => _entries.Add((role, text, null));

    public void Call(string call, string result) => _entries.Add(("tool", call, result));

    /// <summary>Captures the window, composes a frame and holds it for <paramref name="seconds"/>.</summary>
    public async Task<byte[]> Frame(Viewer v, double seconds)
    {
        using var window = SKBitmap.Decode(await v.Window());
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x0b, 0x12, 0x1b));

        // Viewer on the right, scaled to fit.
        var right = new SKRect(PanelWidth + Margin * 2, Margin, Width - Margin, Height - Margin);
        var scale = Math.Min(right.Width / window.Width, right.Height / window.Height);
        var dest = SKRect.Create(right.Left, right.MidY - window.Height * scale / 2, window.Width * scale, window.Height * scale);
        using (var image = SKImage.FromBitmap(window))
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));

        DrawTranscript(canvas, new SKRect(Margin, dest.Top, Margin + PanelWidth, dest.Bottom));

        using var snapshot = surface.Snapshot();
        var png = snapshot.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        _frames.Add((png, seconds));
        return png;
    }

    private void DrawTranscript(SKCanvas canvas, SKRect area)
    {
        using var label = new SKFont(Sans, 15) { Edging = SKFontEdging.Antialias };
        using var prose = new SKFont(Sans, 20) { Edging = SKFontEdging.Antialias };
        using var mono = new SKFont(Mono, 15) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { IsAntialias = true };

        // Lay out bottom-up so the latest entries stay visible.
        var blocks = new List<(float Height, Action<float> Draw)>();
        foreach (var (kind, text, detail) in _entries)
        {
            if (kind == "tool")
            {
                var lines = Wrap(text, mono, area.Width - 28);
                var results = Wrap("→ " + detail, mono, area.Width - 28);
                var h = (lines.Count + results.Count) * 21 + 16;
                blocks.Add((h, top =>
                {
                    paint.Color = new SKColor(0x7f, 0xe0, 0xd4);
                    for (var i = 0; i < lines.Count; i++) canvas.DrawText(lines[i], area.Left + 14, top + 18 + i * 21, mono, paint);
                    paint.Color = new SKColor(0x9f, 0xb6, 0xc9);
                    for (var i = 0; i < results.Count; i++) canvas.DrawText(results[i], area.Left + 14, top + 18 + (lines.Count + i) * 21, mono, paint);
                }));
            }
            else
            {
                var lines = Wrap(text, prose, area.Width - 36);
                var h = lines.Count * 27 + 50;
                var user = kind == "user";
                blocks.Add((h, top =>
                {
                    paint.Color = user ? new SKColor(0x1d, 0x2c, 0x3d) : new SKColor(0x13, 0x22, 0x2a);
                    canvas.DrawRoundRect(SKRect.Create(area.Left, top, area.Width, h - 12), 12, 12, paint);
                    paint.Color = new SKColor(0x7f, 0x9a, 0xb0);
                    canvas.DrawText(user ? "YOU" : "AGENT", area.Left + 18, top + 24, label, paint);
                    paint.Color = SKColors.White;
                    for (var i = 0; i < lines.Count; i++) canvas.DrawText(lines[i], area.Left + 18, top + 50 + i * 27, prose, paint);
                }));
            }
        }

        canvas.Save();
        canvas.ClipRect(area);
        var total = blocks.Sum(b => b.Height);
        var y = total > area.Height ? area.Bottom - total : area.Top;
        foreach (var (height, draw) in blocks)
        {
            draw(y);
            y += height;
        }
        canvas.Restore();
    }

    private static List<string> Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(candidate) > width && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>Writes <c>name.mp4</c> and <c>name.webm</c> (silent, looping-friendly) with ffmpeg.</summary>
    public async Task EncodeAsync(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var work = Directory.CreateTempSubdirectory("soundcharts-clip-").FullName;
        var list = new System.Text.StringBuilder();
        for (var i = 0; i < _frames.Count; i++)
        {
            var file = Path.Combine(work, $"f{i:000}.png");
            await File.WriteAllBytesAsync(file, _frames[i].Png);
            list.AppendLine($"file '{file}'").AppendLine($"duration {_frames[i].Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        // The concat demuxer ignores the last duration unless the last file is repeated.
        list.AppendLine($"file '{Path.Combine(work, $"f{_frames.Count - 1:000}.png")}'");
        var listFile = Path.Combine(work, "frames.txt");
        await File.WriteAllTextAsync(listFile, list.ToString());

        await Ffmpeg("-y", "-f", "concat", "-safe", "0", "-i", listFile, "-vf", "fps=30,format=yuv420p",
            "-c:v", "libx264", "-crf", "23", "-preset", "slow", "-movflags", "+faststart", "-an", Path.Combine(directory, name + ".mp4"));
        await Ffmpeg("-y", "-f", "concat", "-safe", "0", "-i", listFile, "-vf", "fps=30,format=yuv420p",
            "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "36", "-an", Path.Combine(directory, name + ".webm"));
        Directory.Delete(work, recursive: true);
    }

    private static async Task Ffmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg not found (brew install ffmpeg)");
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {(await stderr)[^Math.Min(400, (await stderr).Length)..]}");
    }
}

/// <summary>
/// Writes an S-125 or S-131 GML dataset from the S-57 features loaded in the viewer
/// (public-domain NOAA ENCs, translated to the S-101 model), using describe_feature
/// for geometry and attributes and describe_feature_type for the target bindings.
/// </summary>
static class GmlDeriver
{
    public static async Task WriteAsync(Viewer v, string spec, string prefix, string[] types, Box box, string path)
    {
        var ns = prefix == "S125" ? "http://www.iho.int/S125/1.0" : "http://www.iho.int/S131/1.0";
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        xml.AppendLine($"<!-- Derived by site/capture/capture.cs from NOAA ENC cells (public domain) for the soundcharts.app {spec} tile. Not for navigation. -->");
        xml.AppendLine($"""<{prefix}:Dataset xmlns:{prefix}="{ns}" xmlns:S100="http://www.iho.int/s100gml/5.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="DS_{prefix}_ElliottBay">""");
        xml.AppendLine($"  <S100:DatasetIdentificationInformation><S100:productIdentifier>{spec}</S100:productIdentifier><S100:datasetTitle>Elliott Bay (derived from NOAA ENC)</S100:datasetTitle></S100:DatasetIdentificationInformation>");
        var open = prefix == "S125" ? "" : $"  <{prefix}:members>\n";
        xml.Append(open);
        Member(xml, prefix, $"""<{prefix}:DataCoverage gml:id="coverage"><{prefix}:geometry>{Surface("cov", [(box.South, box.West), (box.South, box.East), (box.North, box.East), (box.North, box.West)])}</{prefix}:geometry></{prefix}:DataCoverage>""");

        var seen = new HashSet<string>();
        var n = 0;
        foreach (var type in types)
        {
            var bindings = await Bindings(v, spec, type);
            if (bindings is null) continue;
            var query = new JsonObject { ["kind"] = "box", ["south"] = box.South, ["west"] = box.West, ["north"] = box.North, ["east"] = box.East };
            var matches = (await v.Call("query_features", new() { ["query"] = query.ToJsonString(), ["featureType"] = type, ["pageSize"] = 500 }))?["features"]?.AsArray() ?? [];
            foreach (var match in matches)
            {
                var d = (await v.Call("describe_feature", new() { ["datasetId"] = match!["datasetId"]!.GetValue<string>(), ["featureId"] = match["featureId"]!.GetValue<string>() }))?["attributes"];
                if (d is null) continue;
                var foid = d["foid"]?.ToJsonString() ?? Guid.NewGuid().ToString();
                if (!seen.Add(type + foid)) continue; // the same feature in overlapping cells
                var geometry = Geometry($"g{++n}", d["geometry"]?["primitive"]?.GetValue<string>() ?? d["geometryPrimitive"]?.GetValue<string>(), d["geometry"]?["coordinates"]?.AsArray(), bindings.Value.Primitives);
                if (geometry is null) continue;
                var body = new System.Text.StringBuilder();
                body.Append($"<{prefix}:{type} gml:id=\"f{n}\"><{prefix}:geometry>{geometry}</{prefix}:geometry>");
                var attributes = d["attributes"]?.AsArray() ?? [];
                for (var i = 0; i < attributes.Count; i++)
                {
                    var code = attributes[i]!["acronym"]?.GetValue<string>() ?? "";
                    var value = attributes[i]!["value"]?.GetValue<string>() ?? "";
                    if (code == "featureName" && bindings.Value.Complex.Contains("featureName"))
                    {
                        string? name = null, language = null;
                        for (var j = i + 1; j < attributes.Count && j <= i + 2; j++)
                        {
                            var sub = attributes[j]!["acronym"]?.GetValue<string>();
                            if (sub == "name") name = attributes[j]!["value"]?.GetValue<string>();
                            else if (sub == "language") language = attributes[j]!["value"]?.GetValue<string>();
                        }
                        if (!string.IsNullOrWhiteSpace(name))
                            body.Append($"<{prefix}:featureName><{prefix}:name>{System.Security.SecurityElement.Escape(name)}</{prefix}:name>{(language is null ? "" : $"<{prefix}:language>{language}</{prefix}:language>")}</{prefix}:featureName>");
                    }
                    else if (value.Length > 0 && bindings.Value.Simple.Contains(code))
                    {
                        body.Append($"<{prefix}:{code}>{System.Security.SecurityElement.Escape(value)}</{prefix}:{code}>");
                    }
                }
                body.Append($"</{prefix}:{type}>");
                Member(xml, prefix, body.ToString());
            }
        }
        if (prefix != "S125") xml.AppendLine($"  </{prefix}:members>");
        xml.AppendLine($"</{prefix}:Dataset>");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, xml.ToString());
        Console.WriteLine($"  wrote {seen.Count} features to {Path.GetFileName(path)}");
    }

    private static void Member(System.Text.StringBuilder xml, string prefix, string feature) =>
        xml.AppendLine(prefix == "S125" ? $"  <{prefix}:member>{feature}</{prefix}:member>" : $"    {feature}");

    /// <summary>Simple and complex attribute codes bound to a type, including its supertypes.</summary>
    private static async Task<(HashSet<string> Simple, HashSet<string> Complex, HashSet<string> Primitives)?> Bindings(Viewer v, string spec, string type)
    {
        var simple = new HashSet<string>();
        var complex = new HashSet<string>();
        HashSet<string>? primitives = null;
        for (string? current = type; current is not null;)
        {
            JsonNode? t;
            try { t = (await v.Call("describe_feature_type", new() { ["spec"] = spec, ["featureType"] = current, ["includeListedValues"] = false }))?["featureTypes"]?[0]; }
            catch (InvalidOperationException) { return primitives is null ? null : (simple, complex, primitives); }
            if (t is null) break;
            primitives ??= t["permittedPrimitives"]?.AsArray().Select(p => p!.GetValue<string>()).ToHashSet() ?? [];
            foreach (var a in t["attributes"]?.AsArray() ?? [])
                (a!["isComplex"]?.GetValue<bool>() == true ? complex : simple).Add(a["code"]!.GetValue<string>());
            current = t["superType"]?.GetValue<string>();
        }
        return primitives is null ? null : (simple, complex, primitives);
    }

    private static string? Geometry(string id, string? primitive, JsonArray? coordinates, HashSet<string> permitted)
    {
        if (coordinates is null || coordinates.Count == 0) return null;
        var points = coordinates.Select(c => (c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
        switch (primitive?.ToLowerInvariant())
        {
            case "point" when permitted.Contains("point"):
                return $"""<S100:pointProperty><gml:Point gml:id="{id}" srsName="EPSG:4326"><gml:pos>{points[0].Item1:0.0000000} {points[0].Item2:0.0000000}</gml:pos></gml:Point></S100:pointProperty>""";
            case "curve" when permitted.Contains("curve") && points.Count > 1:
                return $"""<S100:curveProperty><gml:Curve gml:id="{id}" srsName="EPSG:4326"><gml:segments><gml:LineStringSegment><gml:posList>{PosList(points)}</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></S100:curveProperty>""";
            case "surface" when permitted.Contains("surface") && points.Count > 2:
                return Surface(id, points);
            default:
                return null;
        }
    }

    private static string Surface(string id, IReadOnlyList<(double Lat, double Lon)> ring)
    {
        var closed = ring.ToList();
        if (closed[0] != closed[^1]) closed.Add(closed[0]);
        return $"""<S100:surfaceProperty><gml:Surface gml:id="{id}" srsName="EPSG:4326"><gml:patches><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>{PosList(closed)}</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></S100:surfaceProperty>""";
    }

    private static string PosList(IEnumerable<(double Lat, double Lon)> points) =>
        string.Join(" ", points.Select(p => $"{p.Lat:0.0000000} {p.Lon:0.0000000}"));
}

/// <summary>
/// Maintains <c>manifest.json</c> beside the shots: for each shot, the datasets on
/// screen (file, product, source, licence, edition or forecast run), the recorded
/// viewer calls (viewport, palette, category, panels, clock, …) and when the image
/// and the record were made. Shared viewer settings are stored once.
/// </summary>
static class ShotManifest
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The repository root, so recorded paths can be made machine-independent.</summary>
    public static string RepoRoot { get; set; } = "";

    public static JsonNode? Arguments(Dictionary<string, object?>? arguments)
    {
        if (arguments is null) return null;
        var node = JsonSerializer.SerializeToNode(arguments);
        foreach (var (key, value) in node!.AsObject().ToList())
        {
            if (value is not JsonValue v || !v.TryGetValue<string>(out var text)) continue;
            text = Portable(text);
            // Keep long payloads (e.g. a densified polyline) readable.
            node[key] = text.Length > 160 ? text[..157] + "…" : text;
        }
        return node;
    }

    /// <summary>A path relative to the repo, or to ~, instead of this machine's absolute path.</summary>
    public static string Portable(string path)
    {
        var p = path.Replace('\\', '/');
        var repo = RepoRoot.Replace('\\', '/').TrimEnd('/') + "/";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/').TrimEnd('/') + "/";
        return p.StartsWith(repo, StringComparison.Ordinal) ? p[repo.Length..]
            : p.StartsWith(home, StringComparison.Ordinal) ? "~/" + p[home.Length..]
            : path;
    }

    public static async Task RecordAsync(string manifestPath, string id, Viewer v, string imagePath, string repoRoot, bool recapturedImage)
    {
        JsonObject root;
        try { root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject(); }
        catch (Exception e) when (e is IOException or JsonException) { root = new JsonObject(); }

        root["generatedBy"] = "site/capture/capture.cs";
        root["settings"] = new JsonObject
        {
            ["window"] = "1100×700 logical, captured at 2×",
            ["chromeTheme"] = "Dark",
            ["basemap"] = "Offline (bundled Natural Earth)",
            ["statusBar"] = false,
            ["fourShades"] = Viewer.FourShades,
            ["safetyContourMetres"] = 10,
            ["safetyDepthMetres"] = 10,
            ["hiddenViewingGroups"] = "S-101/S-57: 90020, 90021 (additional-information markers)",
            ["outOfScaleExtentIndicators"] = false,
            ["locale"] = "en_US, invariant number formatting",
            ["timeZone"] = TimeZoneInfo.Local.Id,
        };

        var datasets = new JsonArray();
        foreach (var d in (await v.Call("list_datasets"))?["datasets"]?.AsArray() ?? [])
        {
            var dsId = d!["id"]!.GetValue<string>();
            v.Paths.TryGetValue(dsId, out var path);
            var entry = Source(dsId, path, repoRoot);
            entry["id"] = dsId;
            entry["spec"] = d["spec"]?["name"]?.GetValue<string>();
            entry["bounds"] = d["bounds"]?.DeepClone();
            datasets.Add(entry);
        }

        JsonObject? timeline = null;
        if ((await v.Call("get_timeline_state")) is { } t && t["active"]?.GetValue<bool>() == true)
            timeline = new JsonObject
            {
                ["mode"] = t["mode"]?.DeepClone(), ["viewTime"] = t["viewTime"]?.DeepClone(),
                ["now"] = t["now"]?.DeepClone(), ["runs"] = t["runs"]?.DeepClone(),
            };

        var shots = root["shots"] as JsonObject ?? new JsonObject();
        root["shots"] = shots;
        shots[id] = new JsonObject
        {
            ["image"] = Path.GetFileName(imagePath),
            ["imageCapturedUtc"] = File.Exists(imagePath) ? File.GetLastWriteTimeUtc(imagePath).ToString("O") : null,
            ["recordedUtc"] = DateTime.UtcNow.ToString("O"),
            // False when recorded with --manifest-only: the image is older than this
            // record, so live data (forecast runs, chart editions) may differ from it.
            ["imageMatchesRecord"] = recapturedImage,
            ["chromeTheme"] = "Dark",
            ["capture"] = v.CaptureMode,
            ["datasets"] = datasets,
            ["timeline"] = timeline,
            ["steps"] = v.Steps.DeepClone(),
        };

        // Shots sorted by id so diffs stay small.
        var sorted = new JsonObject();
        foreach (var (key, value) in shots.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList())
        {
            shots.Remove(key);
            sorted[key] = value;
        }
        root["shots"] = sorted;
        await File.WriteAllTextAsync(manifestPath, root.ToJsonString(Indented) + "\n");
    }

    /// <summary>Where a dataset came from, its licence and its edition or forecast run.</summary>
    private static JsonObject Source(string id, string? path, string repoRoot)
    {
        var p = path?.Replace('\\', '/') ?? "";
        string? file = path is null ? null : Portable(path);
        var name = Path.GetFileName(p.Length > 0 ? p : id);
        var run = System.Text.RegularExpressions.Regex.Match(name, @"_(\d{8}T\d{2}Z)_");

        (string Source, string Licence) = 0 switch
        {
            _ when p.Contains("Unencrypted_S100_Datasets") => ("Canadian Hydrographic Service S-100 sample package (Nov 2025)", "CHS licence: non-commercial, CHS notice required, data not redistributed"),
            _ when p.Contains("/tests/datasets/S101/") => ("IHO S-101 test data set (fictional area)", "IHO test data, bundled in this repo"),
            _ when p.Contains("/tests/datasets/") => ("EncDotNet.S100 test fixture", "repo fixture (MIT)"),
            _ when p.Contains("/.cache/derived/") => ("Derived by capture.cs from NOAA ENC cells", "public domain source (NOAA)"),
            _ when p.Contains("/.cache/enc/") => ("NOAA ENC (charts.noaa.gov)", "public domain (NOAA)"),
            _ when p.Contains("/.cache/s102/") => ("NOAA S-102 on AWS (noaa-s102-pds)", "public domain (NOAA)"),
            _ when p.Contains("/.cache/s111/") => ("NOAA S-111 on AWS (noaa-s111-pds)", "public domain (NOAA)"),
            _ when p.Contains("/.cache/s411/S411_NWS_") => ("US National Weather Service ice analysis, via the BSIS Ice Portal (bsis-ice.de)", "public domain (NOAA NWS)"),
            _ when p.Contains("Inland S-57 ENCs") => ("USACE inland ENC (IENC)", "public domain (USACE)"),
            _ when path is null && id.StartsWith("102US", StringComparison.Ordinal) => ("NOAA S-102 on AWS, downloaded by the Library", "public domain (NOAA)"),
            _ when path is null && id.StartsWith("US", StringComparison.Ordinal) => ("NOAA ENC, downloaded by the Library", "public domain (NOAA)"),
            _ => ("unknown", "unknown"),
        };

        var entry = new JsonObject { ["file"] = file ?? name, ["source"] = Source, ["licence"] = Licence };
        if (run.Success) entry["forecastRun"] = run.Groups[1].Value;
        return entry;
    }
}
