using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Samples.MapHost;

/// <summary>
/// Headless end-to-end check of the reusable S-100 session - the same
/// <c>Map.AddS100</c> API the GUI drives, exercised without an Avalonia window
/// so it can run in CI. Run with <c>--smoke</c>.
/// </summary>
/// <remarks>
/// It attaches a session to a bare <see cref="Map"/>, loads the bundled S-101
/// cell, frames it, runs a geographic pick at the cell centre, and disposes
/// everything - asserting the reusable pipeline works from a non-Viewer,
/// non-GUI host. Rendering to pixels is a UI concern and stays with the GUI;
/// this proves dataset load, extent, picking, and teardown.
/// </remarks>
internal static class SmokeTest
{
    public static async Task<int> RunAsync()
    {
        var cellPath = Path.Combine(AppContext.BaseDirectory, "sample-cell.000");
        if (!File.Exists(cellPath))
        {
            Console.Error.WriteLine($"FAIL: bundled cell not found at {cellPath}");
            return 1;
        }

        using var host = SampleS100Host.Create();
        var map = new Map { CRS = "EPSG:3857" };

        await using var session = map.AddS100(
            new ProjNetCrsTransformFactory(),
            new S100MapsuiOptions { DatasetPipelineFactory = host.PipelineFactory });

        // Give the map a viewport so framing and scale-aware picking have a
        // resolution to work with (a live control would supply this).
        map.Navigator.SetSize(1000, 800);

        Console.WriteLine($"Loading {Path.GetFileName(cellPath)} …");
        var id = await session.Datasets.LoadAsync(cellPath);

        var datasets = session.GetDatasets();
        if (datasets.Count != 1)
        {
            Console.Error.WriteLine($"FAIL: expected 1 dataset, found {datasets.Count}.");
            return 1;
        }

        var snapshot = session.GetDataset(id);
        if (snapshot?.Extent is not { } extent)
        {
            Console.Error.WriteLine("FAIL: loaded dataset has no extent.");
            return 1;
        }

        Console.WriteLine(
            $"Loaded {id}: {snapshot.Layers.Count} layer(s), extent "
            + $"[{extent.MinX:F0},{extent.MinY:F0} .. {extent.MaxX:F0},{extent.MaxY:F0}] (EPSG:3857); "
            + $"content cutoff (max visible resolution): "
            + $"{(snapshot.ContentMaxVisibleResolution is { } r ? $"{r:F1} m/px" : "none")}.");

        session.ZoomToDataset(id);
        var resolution = map.Navigator.Viewport.Resolution;

        // Pick at the cell centre. Convert the EPSG:3857 centroid to WGS-84 the
        // same way the Avalonia adapter does for a live pointer.
        var centre = extent.Centroid;
        var (longitude, latitude) = SphericalMercator.ToLonLat(centre.X, centre.Y);
        var picks = await session.Query.PickAsync(new GeographicPickQuery
        {
            Latitude = latitude,
            Longitude = longitude,
            Resolution = resolution,
        });

        Console.WriteLine(
            picks.Count == 0
                ? "Pick at cell centre: no feature (open water is a valid result)."
                : $"Pick at cell centre: {picks.Count} hit(s); topmost "
                    + $"{(picks[0].IsCoverage ? "coverage" : picks[0].FeatureType ?? "feature")}.");

        // Remove the dataset explicitly (asserts teardown of a single dataset);
        // the `await using` then disposes the whole session, releasing its
        // processors, layers, subscriptions, and caches.
        session.RemoveDataset(id);
        if (session.GetDatasets().Count != 0)
        {
            Console.Error.WriteLine("FAIL: dataset still present after removal.");
            return 1;
        }

        Console.WriteLine("Disposing session …");

        Console.WriteLine("PASS: reusable S-100 session loaded, framed, picked, and tore down headlessly.");
        return 0;
    }
}
