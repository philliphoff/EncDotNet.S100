using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios;

/// <summary>One file a scenario wrote.</summary>
/// <param name="ProductIdentifier">The product, such as <c>"S-101"</c>.</param>
/// <param name="RelativePath">The path relative to the exchange set root, with forward slashes, as the catalogue lists it.</param>
/// <param name="FullPath">The file's full path.</param>
public sealed record GeneratedDataset(string ProductIdentifier, string RelativePath, string FullPath);

/// <summary>A scenario written to disk as an exchange set.</summary>
/// <param name="Definition">The scenario.</param>
/// <param name="Root">The exchange set's root folder.</param>
/// <param name="Datasets">The datasets, in catalogue order.</param>
public sealed record GeneratedScenario(ScenarioDefinition Definition, string Root, IReadOnlyList<GeneratedDataset> Datasets)
{
    /// <summary>The <c>CATALOG.XML</c> path.</summary>
    public string CataloguePath => Path.Combine(Root, "CATALOG.XML");
}

/// <summary>
/// Writes a <see cref="ScenarioDefinition"/> as an S-100 exchange set:
/// <c>CATALOG.XML</c> at the root and each product's files under
/// <c>&lt;product&gt;/DATASET_FILES/</c>.
/// </summary>
/// <remarks>
/// Output is deterministic: the same definition gives byte-identical files.
/// Every time written comes from the definition, collections are written in
/// definition order, and random draws come from <see cref="ScenarioRandom"/>
/// streams keyed by the world's seed.
/// </remarks>
public static class ScenarioGenerator
{
    /// <summary>Writes the scenario into <paramref name="directory"/>, creating it if needed.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="directory">The exchange set's root folder.</param>
    /// <returns>What was written.</returns>
    public static GeneratedScenario Generate(ScenarioDefinition definition, string directory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        Directory.CreateDirectory(directory);
        var world = definition.World;
        var frame = world.Frame;
        var issueDate = DateOnly.FromDateTime(definition.Issued);
        var datasets = new List<GeneratedDataset>();
        var entries = new List<CatalogueDataset>();

        string Prepare(string product, string fileName)
        {
            var relative = $"{product}/DATASET_FILES/{fileName}";
            var full = Path.Combine(directory, product, "DATASET_FILES", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            datasets.Add(new GeneratedDataset(product, relative, full));
            return full;
        }

        foreach (var cell in definition.Cells)
        {
            var path = Prepare("S-101", cell.FileName);
            S101DocumentWriter.WriteToFile(path, S101CellWriter.Build(world, cell, definition.DredgedChannels, issueDate));
            var sw = frame.ToGeo(cell.Extent.West, cell.Extent.South);
            var ne = frame.ToGeo(cell.Extent.East, cell.Extent.North);
            entries.Add(new CatalogueDataset(
                datasets[^1].RelativePath, "S-101", "Electronic Navigational Chart", "2.0.0", "ISO/IEC 8211", issueDate,
                sw.Longitude, sw.Latitude, ne.Longitude, ne.Latitude, cell.Title));
        }

        foreach (var surface in definition.Surfaces)
        {
            var path = Prepare("S-102", surface.FileName);
            CoverageWriters.WriteS102(world, surface, path, definition.Issued);
            var grid = CoverageWriters.Grid(world, surface);
            entries.Add(new CatalogueDataset(
                datasets[^1].RelativePath, "S-102", "Bathymetric Surface", "3.0.0", "HDF5", issueDate,
                grid.OriginLongitude, grid.OriginLatitude, grid.EastLongitude, grid.NorthLatitude,
                $"SYNTHETIC bathymetric surface of {world.Name} (fictitious)."));
        }

        if (definition.Stations is { } stations)
        {
            var path = Prepare("S-104", stations.FileName);
            CoverageWriters.WriteS104Stations(world, stations, path, definition.Issued, definition.WaterLevelStart, definition.WaterLevelEnd);
            var positions = world.Tide.Stations.Select(s => frame.ToGeo(s.X, s.Y)).ToList();
            entries.Add(new CatalogueDataset(
                datasets[^1].RelativePath, "S-104", "Water Level Information for Surface Navigation", "2.0.0", "HDF5", issueDate,
                positions.Min(p => p.Longitude), positions.Min(p => p.Latitude), positions.Max(p => p.Longitude), positions.Max(p => p.Latitude),
                $"SYNTHETIC tide predictions at {world.Name} stations (fictitious)."));
        }

        foreach (var waterLevel in definition.WaterLevelGrids)
        {
            var path = Prepare("S-104", waterLevel.FileName);
            CoverageWriters.WriteS104Grid(world, waterLevel, path, definition.Issued, definition.WaterLevelStart, definition.WaterLevelEnd);
            var grid = CoverageWriters.Grid(world, waterLevel);
            entries.Add(new CatalogueDataset(
                datasets[^1].RelativePath, "S-104", "Water Level Information for Surface Navigation", "2.0.0", "HDF5", issueDate,
                grid.OriginLongitude, grid.OriginLatitude, grid.EastLongitude, grid.NorthLatitude,
                $"SYNTHETIC tide prediction grid over {world.Name} (fictitious)."));
        }

        var seed = world.Seed.ToString(CultureInfo.InvariantCulture);
        File.WriteAllBytes(
            Path.Combine(directory, "CATALOG.XML"),
            ExchangeCatalogueBuilder.Build(
                $"SYNTHETIC {definition.Name} seed {seed}",
                definition.Issued,
                $"SYNTHETIC test data: scenario '{definition.Name}', seed {seed}, in {world.Name}, a fictitious area. Not for navigation.",
                entries));

        return new GeneratedScenario(definition, directory, datasets);
    }
}
