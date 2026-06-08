using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.Tests.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="VesselListViewModel"/> — the AIS Vessels panel.
/// Runs with no Avalonia dispatcher, so source events refresh the list
/// synchronously.
/// </summary>
public class VesselListViewModelTests
{
    private static FakeDynamicFeatureSource NewAisSource()
        => new("ais", new DynamicSourceMetadata { DisplayName = "AIS", RendererKey = "vessel.ais" });

    private static FakeDynamicFeatureSource NewOwnShipSource()
        => new(OwnShipSource.FeatureId, new DynamicSourceMetadata
        {
            DisplayName = "Own ship",
            RendererKey = OwnShipSource.FeatureKind,
        });

    private static DynamicFeature OwnFeature(double lat, double lon)
        => new()
        {
            Id = OwnShipSource.FeatureId,
            Kind = OwnShipSource.FeatureKind,
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (lat, lon) },
            LastUpdated = DateTimeOffset.UnixEpoch,
        };

    private static DynamicFeature Vessel(
        uint mmsi,
        double lat,
        double lon,
        string? name = null,
        AisNavigationStatus? navStatus = null,
        double? sogKn = null,
        string? destination = null,
        AisShipTypeClass shipTypeClass = AisShipTypeClass.Unknown,
        string? callSign = null,
        uint? imoNumber = null,
        double? headingDeg = null,
        double? courseDeg = null,
        double? rateOfTurnDegPerMin = null,
        DateTimeOffset? eta = null,
        double? draughtMetres = null,
        double? lengthMetres = null,
        double? beamMetres = null)
    {
        var attrs = new Dictionary<string, object?> { ["mmsi"] = mmsi, ["shipTypeClass"] = shipTypeClass };
        if (name is not null) attrs["vesselName"] = name;
        if (navStatus is { } ns) attrs["navigationStatus"] = ns;
        if (destination is not null) attrs["destination"] = destination;
        if (callSign is not null) attrs["callSign"] = callSign;
        if (imoNumber is { } imo) attrs["imoNumber"] = imo;
        if (rateOfTurnDegPerMin is { } rot) attrs["rateOfTurnDegPerMin"] = rot;
        if (eta is { } e) attrs["eta"] = e;
        if (draughtMetres is { } d) attrs["draughtMetres"] = d;

        DynamicMotion? motion = null;
        if (sogKn is not null || headingDeg is not null || courseDeg is not null)
        {
            motion = new DynamicMotion
            {
                SpeedOverGroundKn = sogKn,
                HeadingDeg = headingDeg,
                CourseOverGroundDeg = courseDeg,
            };
        }

        DynamicVesselGeometry? geometry = null;
        if (lengthMetres is { } len && beamMetres is { } beam)
        {
            geometry = new DynamicVesselGeometry
            {
                LengthMetres = len,
                BeamMetres = beam,
                BowOffsetMetres = len / 2,
                PortOffsetMetres = beam / 2,
            };
        }

        return new DynamicFeature
        {
            Id = $"ais:{mmsi}",
            Kind = "vessel.ais.unknown",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (lat, lon) },
            Motion = motion,
            VesselGeometry = geometry,
            Attributes = attrs,
            LastUpdated = DateTimeOffset.UnixEpoch,
        };
    }

    private static void Raise(FakeDynamicFeatureSource source, DynamicSourceChangeKind kind = DynamicSourceChangeKind.Added)
        => source.RaiseChanged(new DynamicFeaturesChanged { Kind = kind, ChangedIds = Array.Empty<string>() });

    /// <summary>
    /// Builds a VM wired to a fresh AIS source and an own-ship source. By
    /// default the own ship is "enabled" (publishing a position feature at
    /// <paramref name="ownLat"/>/<paramref name="ownLon"/>); set
    /// <paramref name="ownShipEnabled"/> false to model the overlay being
    /// switched off, or <paramref name="includeOwnShip"/> false to omit the
    /// source entirely.
    /// </summary>
    private static (VesselListViewModel vm, FakeDynamicFeatureSource ais,
        FakeDynamicFeatureSource ownShip, FakeMapHost host) Make(
        bool ownShipEnabled = true,
        double ownLat = 0,
        double ownLon = 0,
        bool includeOwnShip = true,
        FakeDynamicFeatureSource? aisSource = null)
    {
        var ais = aisSource ?? NewAisSource();
        var ownShip = NewOwnShipSource();
        if (ownShipEnabled)
        {
            ownShip.SetFeatures(new[] { OwnFeature(ownLat, ownLon) });
        }

        var host = new FakeMapHost();
        var accessor = new MapHostAccessor { Current = host };

        var sources = includeOwnShip
            ? new IDynamicFeatureSource[] { ais, ownShip }
            : new IDynamicFeatureSource[] { ais };

        var vm = new VesselListViewModel(sources, accessor);
        return (vm, ais, ownShip, host);
    }

    [Fact]
    public void Lists_AisFeatures_NearestFirst()
    {
        var (vm, ais, _, _) = Make();

        // A is ~10° away, B is ~1° away.
        ais.SetFeatures(new[] { Vessel(111, 10, 0, "Far"), Vessel(222, 1, 0, "Near") });
        Raise(ais);

        Assert.Equal(2, vm.Vessels.Count);
        Assert.Equal("Near", vm.Vessels[0].Name);
        Assert.Equal("Far", vm.Vessels[1].Name);
        Assert.True(vm.Vessels[0].DistanceMetres < vm.Vessels[1].DistanceMetres);
    }

    [Fact]
    public void PreExistingFeatures_ListedOnConstruction()
    {
        var ais = NewAisSource();
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Already") });

        var (vm, _, _, _) = Make(aisSource: ais);

        Assert.Single(vm.Vessels);
        Assert.Equal("Already", vm.Vessels[0].Name);
    }

    [Fact]
    public void Bearing_DueNorthIsZero_DueEastIsNinety()
    {
        var (vm, ais, _, _) = Make();
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "North"), Vessel(2, 0, 1, "East") });
        Raise(ais);

        var north = vm.Vessels.Single(v => v.Name == "North");
        var east = vm.Vessels.Single(v => v.Name == "East");
        Assert.True(north.HasRangeBearing);
        Assert.Equal("000°", north.BearingText);
        Assert.Equal("090°", east.BearingText);
    }

    [Fact]
    public void SelectingVessel_CentersMapOnIt()
    {
        var (vm, ais, _, host) = Make();
        ais.SetFeatures(new[] { Vessel(1, 12.5, -7.25, "Target") });
        Raise(ais);

        vm.SelectedVessel = vm.Vessels[0];

        var call = Assert.Single(host.CenterOnCalls);
        Assert.Equal(12.5, call.Latitude, 6);
        Assert.Equal(-7.25, call.Longitude, 6);
    }

    [Fact]
    public void Destination_ShownWhenPresent_RegardlessOfState()
    {
        // In the master/detail layout the destination lives in the detail
        // pane and is shown whenever it is known, not only while moving.
        var (vm, ais, _, _) = Make();
        ais.SetFeatures(new[]
        {
            Vessel(1, 1, 0, "Mover", AisNavigationStatus.UnderWayUsingEngine, sogKn: 12, destination: "ROTTERDAM"),
            Vessel(2, 2, 0, "Moored", AisNavigationStatus.Moored, sogKn: 0, destination: "HAMBURG"),
            Vessel(3, 3, 0, "Quiet", AisNavigationStatus.Moored, sogKn: 0),
        });
        Raise(ais);

        var mover = vm.Vessels.Single(v => v.Name == "Mover");
        var moored = vm.Vessels.Single(v => v.Name == "Moored");
        var quiet = vm.Vessels.Single(v => v.Name == "Quiet");

        Assert.True(mover.HasDestination);
        Assert.Equal("ROTTERDAM", mover.DestinationText);
        Assert.True(moored.HasDestination);
        Assert.Equal("HAMBURG", moored.DestinationText);
        Assert.False(quiet.HasDestination);
        Assert.Null(quiet.DestinationText);
    }

    [Fact]
    public void DetailFields_PopulateFromStaticAndDynamicData()
    {
        var (vm, ais, _, _) = Make();
        ais.SetFeatures(new[]
        {
            Vessel(
                123456789, 1, 0, "Atlantic",
                navStatus: AisNavigationStatus.UnderWayUsingEngine,
                sogKn: 14.2,
                destination: "ROTTERDAM",
                shipTypeClass: AisShipTypeClass.Cargo,
                callSign: "ABCD",
                imoNumber: 9876543,
                headingDeg: 91,
                courseDeg: 95,
                rateOfTurnDegPerMin: 6,
                eta: new DateTimeOffset(2024, 3, 14, 9, 30, 0, TimeSpan.Zero),
                draughtMetres: 8.5,
                lengthMetres: 200,
                beamMetres: 30),
        });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.Equal("Cargo", item.ShipTypeText);
        Assert.Equal("123456789", item.MmsiText);
        Assert.True(item.HasCallSign);
        Assert.Equal("ABCD", item.CallSign);
        Assert.True(item.HasImo);
        Assert.Equal("9876543", item.ImoText);

        Assert.True(item.HasMotion);
        Assert.True(item.HasSpeed);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, Strings.Vessels_SpeedFormat, 14.2),
            item.SpeedText);
        Assert.True(item.HasHeading);
        Assert.Equal("091°", item.HeadingText);
        Assert.True(item.HasCourse);
        Assert.Equal("095°", item.CourseText);
        Assert.True(item.HasRateOfTurn);

        Assert.True(item.HasVoyage);
        Assert.True(item.HasDestination);
        Assert.Equal("ROTTERDAM", item.DestinationText);
        Assert.True(item.HasEta);
        Assert.Contains("09:30", item.EtaText);
        Assert.True(item.HasDraught);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, Strings.Vessels_DraughtFormat, 8.5),
            item.DraughtText);

        Assert.True(item.HasDimensionsSection);
        Assert.True(item.HasDimensions);
        Assert.Contains("200", item.DimensionsText);
        Assert.Contains("30", item.DimensionsText);

        // Type and status are surfaced together in the detail header.
        Assert.Contains("Cargo", item.HeaderSubtitle);
        Assert.Contains(item.StateText, item.HeaderSubtitle);
    }

    [Fact]
    public void DetailFields_HiddenWhenDataAbsent()
    {
        var (vm, ais, _, _) = Make(ownShipEnabled: false);
        // Minimal target: only an MMSI and a position.
        ais.SetFeatures(new[] { Vessel(42, 1, 0) });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.Equal("42", item.MmsiText);
        Assert.Equal("Unknown", item.ShipTypeText);
        Assert.False(item.HasCallSign);
        Assert.False(item.HasImo);
        Assert.False(item.HasMotion);
        Assert.False(item.HasSpeed);
        Assert.False(item.HasHeading);
        Assert.False(item.HasCourse);
        Assert.False(item.HasRateOfTurn);
        Assert.False(item.HasVoyage);
        Assert.False(item.HasDestination);
        Assert.False(item.HasEta);
        Assert.False(item.HasDraught);
        Assert.False(item.HasDimensions);
        Assert.False(item.HasDimensionsSection);
        Assert.False(item.HasRangeBearing);
    }

    [Fact]
    public void Draught_BelongsToDimensionsSection_NotVoyage()
    {
        var (vm, ais, _, _) = Make(ownShipEnabled: false);
        // Draught present but no destination/ETA and no hull dimensions.
        ais.SetFeatures(new[] { Vessel(7, 1, 0, "Deep", draughtMetres: 11.2) });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.True(item.HasDraught);
        Assert.False(item.HasVoyage);
        Assert.True(item.HasDimensionsSection);
    }

    [Fact]
    public void Selection_SurvivesResortWhenOrderChanges()
    {
        // Own ship off and no viewport centre, so ordering falls back to
        // name; a new arrival changes a vessel's sort position and triggers
        // a reconcile (Move/Insert).
        var (vm, ais, _, _) = Make(ownShipEnabled: false);
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Mike") });
        Raise(ais);

        var selected = vm.Vessels[0];
        vm.SelectedVessel = selected;
        Assert.True(vm.HasSelection);

        // A second vessel arrives that sorts before the selection by name,
        // pushing the selected row to a new index.
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Mike"), Vessel(2, 2, 0, "Alpha") });
        Raise(ais);

        Assert.Equal("Alpha", vm.Vessels[0].Name);
        // The selection is preserved by reference across the re-sort.
        Assert.Same(selected, vm.SelectedVessel);
        Assert.True(vm.HasSelection);
    }

    [Fact]
    public void OrdersByViewportCenter_WhenOwnShipDisabled()
    {
        var (vm, ais, _, host) = Make(ownShipEnabled: false);
        // The viewport is centred near vessel "Near" (10,10); vessel "Far"
        // sits far away at (0,0). With no own ship, ordering should key off
        // the viewport centre, not MMSI (whose order would put 100 first).
        host.ViewportCenter = (10, 10);
        ais.SetFeatures(new[] { Vessel(100, 0, 0, "Far"), Vessel(900, 10.1, 10, "Near") });
        Raise(ais);

        Assert.Equal("Near", vm.Vessels[0].Name);
        Assert.Equal("Far", vm.Vessels[1].Name);
        // Range/bearing stay hidden — they require an own-ship reference.
        Assert.False(vm.Vessels[0].HasRangeBearing);
    }

    [Fact]
    public void Selection_TracksHasSelection()
    {
        var (vm, ais, _, _) = Make();
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Target") });
        Raise(ais);

        Assert.False(vm.HasSelection);
        vm.SelectedVessel = vm.Vessels[0];
        Assert.True(vm.HasSelection);
        vm.SelectedVessel = null;
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void OwnShipDisabled_HidesRangeAndBearing()
    {
        // Own-ship source present but publishing no feature == overlay off.
        var (vm, ais, _, _) = Make(ownShipEnabled: false);
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Lonely") });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.False(item.HasRangeBearing);
        Assert.Null(item.DistanceMetres);
        Assert.Equal(string.Empty, item.RangeBearingText);
    }

    [Fact]
    public void NoOwnShipSource_HidesRangeAndBearing()
    {
        var (vm, ais, _, _) = Make(includeOwnShip: false);
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Lonely") });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.False(item.HasRangeBearing);
        Assert.Null(item.DistanceMetres);
    }

    [Fact]
    public void EnablingOwnShip_RevealsRangeAndBearing()
    {
        var (vm, ais, ownShip, _) = Make(ownShipEnabled: false);
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Target") });
        Raise(ais);
        Assert.False(vm.Vessels[0].HasRangeBearing);

        // Own-ship overlay toggled on: it publishes a feature and raises.
        ownShip.SetFeatures(new[] { OwnFeature(0, 0) });
        Raise(ownShip);

        Assert.True(vm.Vessels[0].HasRangeBearing);
        Assert.NotNull(vm.Vessels[0].DistanceMetres);
    }

    [Fact]
    public void RemovedSelectedVessel_ClearsSelection_WithoutRecentering()
    {
        var (vm, ais, _, host) = Make();
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Gone") });
        Raise(ais);

        vm.SelectedVessel = vm.Vessels[0];
        Assert.Single(host.CenterOnCalls);

        ais.SetFeatures(Array.Empty<DynamicFeature>());
        Raise(ais, DynamicSourceChangeKind.Removed);

        Assert.Null(vm.SelectedVessel);
        Assert.Empty(vm.Vessels);
        Assert.Single(host.CenterOnCalls); // no additional center call on removal
    }

    [Fact]
    public void InvalidCoordinates_AreIgnored()
    {
        var (vm, ais, _, _) = Make();
        ais.SetFeatures(new[]
        {
            Vessel(1, double.NaN, 0, "Bad"),
            Vessel(2, 95, 0, "OutOfRange"),
            Vessel(3, 1, 0, "Good"),
        });
        Raise(ais);

        var item = Assert.Single(vm.Vessels);
        Assert.Equal("Good", item.Name);
    }

    [Fact]
    public void NoAisSource_PanelIsEmpty_NoThrow()
    {
        var accessor = new MapHostAccessor { Current = new FakeMapHost() };

        // Only an own-ship-keyed source; no "vessel.ais" source present.
        var other = NewOwnShipSource();
        other.SetFeatures(new[] { OwnFeature(0, 0) });

        var vm = new VesselListViewModel(new IDynamicFeatureSource[] { other }, accessor);

        Assert.Empty(vm.Vessels);
    }

    [Fact]
    public void OwnShipMove_RecomputesDistances()
    {
        var (vm, ais, ownShip, _) = Make(ownLat: 5, ownLon: 0);
        ais.SetFeatures(new[] { Vessel(1, 1, 0, "Target") });
        Raise(ais);

        var before = vm.Vessels[0].DistanceMetres;

        // Move own ship right next to the target.
        ownShip.SetFeatures(new[] { OwnFeature(1, 0) });
        Raise(ownShip, DynamicSourceChangeKind.Updated);

        var after = vm.Vessels[0].DistanceMetres;
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after < before);
    }

    [Fact]
    public void EmptyState_AisDisabled_ShowsEnableMessage()
    {
        // A DisabledAisFeatureSource models the overlay being switched off.
        var disabled = new DisabledAisFeatureSource();
        var accessor = new MapHostAccessor { Current = new FakeMapHost() };

        var vm = new VesselListViewModel(new IDynamicFeatureSource[] { disabled }, accessor);

        Assert.True(vm.IsEmpty);
        Assert.Equal(Strings.Vessels_Empty_Disabled, vm.EmptyMessage);
    }

    [Fact]
    public void EmptyState_AisActiveButNoData_ShowsWaitingMessage()
    {
        // An active AIS source with no features yet (e.g. zoom-gated).
        var (vm, _, _, _) = Make();

        Assert.True(vm.IsEmpty);
        Assert.Equal(Strings.Vessels_Empty_NoData, vm.EmptyMessage);
    }

    [Fact]
    public void EmptyState_NoAisSource_ShowsEnableMessage()
    {
        var accessor = new MapHostAccessor { Current = new FakeMapHost() };
        // A source with an unrelated renderer key — the VM resolves no
        // "vessel.ais" source, so the overlay is treated as off.
        var misc = new FakeDynamicFeatureSource(
            "misc", new DynamicSourceMetadata { DisplayName = "Misc", RendererKey = "other" });

        var vm = new VesselListViewModel(new IDynamicFeatureSource[] { misc }, accessor);

        Assert.True(vm.IsEmpty);
        Assert.Equal(Strings.Vessels_Empty_Disabled, vm.EmptyMessage);
    }
}
