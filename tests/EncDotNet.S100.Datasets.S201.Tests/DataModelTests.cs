using System.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S201.DataModel;

namespace EncDotNet.S100.Datasets.S201.Tests;

public class DataModelTests
{
    private static string GetTestDataPath(string filename)
    {
        var basePath = AppContext.BaseDirectory;
        return Path.Combine(basePath, "TestData", filename);
    }

    private static S201Dataset OpenFixture(string filename) => S201Dataset.Open(GetTestDataPath(filename));

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var empty = new S201Dataset
        {
            ProductIdentifier = "S-201",
            DatasetIdentifier = "EMPTY",
            Features = System.Collections.Immutable.ImmutableArray<S201Feature>.Empty,
            InformationTypes = System.Collections.Immutable.ImmutableArray<S201InformationType>.Empty,
        };
        Assert.Throws<InvalidOperationException>(() => S201AtonInventory.From(empty, out _));
    }

    [Fact]
    public void From_PointDataset_ProjectsLateralBuoyWithStatusInformation()
    {
        var ds = OpenFixture("aton_point.gml");
        var inv = S201AtonInventory.From(ds, out var diagnostics);

        Assert.Equal("S-201", inv.ProductIdentifier);
        Assert.Equal(2, inv.AtoNs.Length);

        var buoy = Assert.IsType<S201StructureObject>(inv.AtoNs.Single(a => a.Id == "f1"));
        Assert.Equal("LateralBuoy", buoy.FeatureClass);
        Assert.Equal(S201GeometryKind.Point, buoy.GeometryKind);
        Assert.Single(buoy.Coordinates);

        // AtoNStatus information binding resolved.
        Assert.Single(buoy.StatusInformation);
        Assert.Equal("info1", buoy.StatusInformation[0].Id);
        Assert.Equal(1, buoy.StatusInformation[0].ChangeTypes);

        // categoryOfLateralMark is not consumed by the structure projection;
        // it round-trips through extras.
        Assert.True(buoy.ExtraAttributes.ContainsKey("categoryOfLateralMark"));

        // Landmark has a featureName complex attribute.
        var landmark = Assert.IsType<S201StructureObject>(inv.AtoNs.Single(a => a.Id == "f2"));
        Assert.Single(landmark.FeatureNames);
        Assert.Equal("Cape Henry Light", landmark.FeatureNames[0].Name);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void From_XlinkDataset_ResolvesEquipmentToHostStructure()
    {
        var ds = OpenFixture("aton_xlink.gml");
        var inv = S201AtonInventory.From(ds, out _);

        var structure = Assert.IsType<S201StructureObject>(inv.AtoNs.Single(a => a.Id == "structure1"));
        var light = Assert.IsType<S201Light>(inv.AtoNs.Single(a => a.Id == "equipment1"));

        Assert.NotNull(light.HostStructure);
        Assert.Equal("structure1", light.HostStructure!.Id);
        Assert.Contains(light, structure.MountedEquipment);
    }

    [Fact]
    public void From_XlinkDataset_ProjectsAggregationWithResolvedPeers()
    {
        var ds = OpenFixture("aton_xlink.gml");
        var inv = S201AtonInventory.From(ds, out _);

        Assert.Single(inv.Aggregations);
        var agg = inv.Aggregations[0];
        Assert.Equal("agg1", agg.Id);
        Assert.Equal(2, agg.Peers.Length);
        Assert.Contains(agg.Peers, p => p.Id == "structure1");
        Assert.Contains(agg.Peers, p => p.Id == "equipment1");

        // Back-fill: each peer should report this aggregation in its Aggregations list.
        var structure = inv.AtoNs.Single(a => a.Id == "structure1");
        Assert.Contains(agg, structure.Aggregations);
    }

    [Fact]
    public void From_UnresolvedXlink_EmitsDiagnosticNoThrow()
    {
        var ds = OpenFixture("aton_unresolved.gml");
        var inv = S201AtonInventory.From(ds, out var diagnostics);

        var orphan = Assert.IsType<S201Light>(inv.AtoNs.Single(a => a.Id == "orphanLight"));
        Assert.Null(orphan.HostStructure);

        Assert.Contains(diagnostics, d =>
            d.Code == "xlink.unresolved" && d.RelatedId == "orphanLight");
    }

    [Fact]
    public void From_AisAtonDataset_DistinguishesVirtualAndPhysical()
    {
        var ds = OpenFixture("aton_ais.gml");
        var inv = S201AtonInventory.From(ds, out _);

        Assert.Equal(2, inv.ElectronicAtoNs.Length);

        var virtualAis = inv.ElectronicAtoNs.Single(e => e.Id == "virtualAis");
        Assert.Equal(AisAtonKind.Virtual, virtualAis.Kind);
        Assert.Equal("993672111", virtualAis.MmsiCode);
        Assert.Null(virtualAis.HostStructure);

        var physicalAis = inv.ElectronicAtoNs.Single(e => e.Id == "physicalAis");
        Assert.Equal(AisAtonKind.Physical, physicalAis.Kind);
        Assert.Equal("993672222", physicalAis.MmsiCode);
        Assert.NotNull(physicalAis.HostStructure);
        Assert.Equal("hostBuoy", physicalAis.HostStructure!.Id);

        Assert.Contains(1, physicalAis.Status);
    }

    [Fact]
    public void From_LightDataset_TypesLightAndLifecycleAttributes()
    {
        var ds = OpenFixture("aton_light.gml");
        var inv = S201AtonInventory.From(ds, out _);

        var house = Assert.IsType<S201StructureObject>(inv.AtoNs.Single(a => a.Id == "house1"));
        Assert.Equal("USCG-99001", house.AtoNNumber);
        Assert.Equal(1, house.AidAvailabilityCategory);
        Assert.NotNull(house.InstallationDate);
        Assert.Equal(1995, house.InstallationDate!.Value.Year);
        Assert.NotNull(house.FixedDateRange);
        Assert.NotNull(house.FixedDateRange!.Start);
        Assert.NotNull(house.FixedDateRange.End);
        Assert.Equal(2099, house.FixedDateRange.End!.Value.Year);

        var light = Assert.IsType<S201Light>(inv.AtoNs.Single(a => a.Id == "lamp1"));
        Assert.Equal(LightKind.AllAround, light.Kind);
        Assert.Equal(26.5, light.Height);
        Assert.Equal(30, light.VerticalDatum);
        Assert.Equal(3.0, light.VerticalLength);
        Assert.Equal(1200.0, light.EffectiveIntensity);
        Assert.Equal(1500.0, light.PeakIntensity);
        Assert.Equal(new[] { 1, 5 }, light.Status.ToArray());
        Assert.NotNull(light.HostStructure);
        Assert.Equal("house1", light.HostStructure!.Id);
    }

    [Fact]
    public void From_LightDataset_RoundTripsUnknownAttributes()
    {
        var ds = OpenFixture("aton_light.gml");
        var inv = S201AtonInventory.From(ds, out _);

        var house = inv.AtoNs.Single(a => a.Id == "house1");
        Assert.True(house.ExtraAttributes.ContainsKey("customExperimentalAttr"));
        Assert.Equal("experiment-value", house.ExtraAttributes["customExperimentalAttr"]);
    }

    [Fact]
    public void From_PointDataset_ExposesTypedViewsOverAtoNs()
    {
        var ds = OpenFixture("aton_point.gml");
        var inv = S201AtonInventory.From(ds, out _);

        // Both LateralBuoy and Landmark are structures.
        Assert.Equal(2, inv.Structures.Length);
        Assert.Empty(inv.Equipment);
        Assert.Empty(inv.ElectronicAtoNs);
        Assert.Single(inv.StatusInformation);
    }

    [Fact]
    public void From_SurfaceDataset_ProjectsDataCoverageGenerically()
    {
        var ds = OpenFixture("aton_surface.gml");
        var inv = S201AtonInventory.From(ds, out _);

        // DataCoverage is in the "deliberately omitted" set — should fall through
        // to S201GenericAtonObject with the geometry preserved.
        var coverage = inv.AtoNs.SingleOrDefault(a => a.FeatureClass == "DataCoverage");
        Assert.NotNull(coverage);
        Assert.IsType<S201GenericAtonObject>(coverage);
        Assert.Equal(S201GeometryKind.Surface, coverage!.GeometryKind);
        Assert.NotEmpty(coverage.Coordinates);
    }
}
