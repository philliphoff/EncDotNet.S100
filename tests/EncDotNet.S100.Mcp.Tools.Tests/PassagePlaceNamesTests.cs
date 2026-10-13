using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>Place names for Check passage, from synthetic S-101 named areas.</summary>
public class PassagePlaceNamesTests
{
    private static S101AreaSynth.AreaFeature Named(string type, string name, (double Lat, double Lon)[] ring) =>
        new(type, ring, new Dictionary<string, string> { ["name"] = name });

    private static PassagePlaceNames Names() => PassagePlaceNames.FromDatasets(
    [
        LoadedDatasetFactory.S101(
            "101NAMES",
            S101AreaSynth.Cell(
            [
                Named("SeaAreaNamedWaterArea", "The Solent", S101AreaSynth.Box(0, 0, 0.1, 0.1)),
                Named("Fairway", "Thorn Channel", S101AreaSynth.Box(0.04, 0.04, 0.05, 0.06)),
                Named("LandRegion", "Calshot", S101AreaSynth.Box(0.055, 0.045, 0.06, 0.05)),
            ]),
            LoadedDatasetFactory.Box(0, 0, 0.1, 0.1)),
    ]);

    [Fact]
    public void The_smallest_named_water_and_the_nearest_land_name_a_place()
    {
        var names = Names();

        Assert.Equal("Thorn Channel · off Calshot", names.NameAt(new GeoPosition(0.045, 0.05)));
        Assert.Equal("The Solent", names.NameAt(new GeoPosition(0.01, 0.09)));
    }

    [Fact]
    public void Nothing_named_nearby_is_null()
    {
        Assert.Null(Names().NameAt(new GeoPosition(1, 1)));
    }
}
