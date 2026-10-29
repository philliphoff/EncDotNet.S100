using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

public sealed class NdbcStationTableTests
{
    private static NdbcStationTable Load()
    {
        using var stream = File.OpenRead(NdbcFixtures.PathOf("activestations.xml"));
        return NdbcStationTable.Parse(stream);
    }

    [Fact]
    public void Reads_station_ids_positions_and_flags()
    {
        var table = Load();

        var buoy = Assert.Single(table.Stations, s => s.Id == NdbcFixtures.Buoy);
        Assert.Equal(42.346, buoy.Latitude, 3);
        Assert.Equal(-70.651, buoy.Longitude, 3);
        Assert.True(buoy.HasMeteorology);
        Assert.True(buoy.IsBuoy);
        Assert.StartsWith("BOSTON", buoy.Name, StringComparison.Ordinal);

        // C-MAN ids are upper cased to match the realtime2 file names.
        var cman = Assert.Single(table.Stations, s => s.Id == NdbcFixtures.Cman);
        Assert.True(cman.IsFixed);
        Assert.True(cman.HasMeteorology);

        Assert.False(Assert.Single(table.Stations, s => s.Id == "44403").HasMeteorology);
    }

    [Fact]
    public void Skips_entries_without_a_position()
    {
        Assert.DoesNotContain(Load().Stations, s => s.Id == "NOLAT");
    }

    [Fact]
    public void Entries_without_a_met_attribute_have_no_meteorology()
    {
        // TAO buoys carry only id, position, elev, name, owner, pgm and type,
        // and have no realtime2 file.
        var tao = Assert.Single(Load().Stations, s => s.Id == "32303");

        Assert.False(tao.HasMeteorology);
        Assert.Equal("tao", tao.Type);
        Assert.Equal(5, tao.Latitude);
        Assert.Equal(-95, tao.Longitude);
    }

    [Fact]
    public void Names_are_unescaped()
    {
        var rig = Assert.Single(Load().Stations, s => s.Id == "KGBK");

        Assert.Equal("Garden Banks 783 / Magnolia TLP (W&T Offshore)", rig.Name);
        Assert.True(rig.HasMeteorology);
    }

    [Fact]
    public void Reads_every_well_formed_entry()
    {
        Assert.Equal(
            ["32303", "44013", "44020", "44403", "46026", "BUZM3", "KGBK"],
            Load().Stations.Select(s => s.Id));
    }

    [Fact]
    public void Lists_meteorological_stations_in_an_area()
    {
        var table = Load();

        var ids = table.MeteorologicalStationsWithin(new BoundingBox(40, -72, 43, -69)).Select(s => s.Id).ToArray();

        Assert.Contains(NdbcFixtures.Buoy, ids);
        Assert.Contains(NdbcFixtures.Cman, ids);
        Assert.DoesNotContain("46026", ids);
        Assert.DoesNotContain("44403", ids);
    }

    [Fact]
    public void An_area_can_cross_the_antimeridian()
    {
        var table = new NdbcStationTable(
        [
            new NdbcStation("51101", "NW Hawaii", 24.3, -162.1) { HasMeteorology = true },
            new NdbcStation("52200", "Guam", 13.35, 144.79) { HasMeteorology = true },
            new NdbcStation("46026", "San Francisco", 37.75, -122.84) { HasMeteorology = true },
        ]);

        var ids = table.MeteorologicalStationsWithin(new BoundingBox(0, 140, 30, -150)).Select(s => s.Id).ToArray();

        Assert.Equal(["51101", "52200"], ids);
    }
}
