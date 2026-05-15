using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Tests for the strongly-typed <see cref="S421RoutePlan"/> projection
/// of an <see cref="S421Dataset"/>.
/// </summary>
public class S421RoutePlanTests
{
    private const string TestDataDir = "TestData";

    private static S421Dataset Load(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S421Dataset.Open(path);
    }

    private static S421RoutePlan Project(string fileName, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
        => S421RoutePlan.From(Load(fileName), out diagnostics);

    // ── Basic projection (GMIN) ──────────────────────────────────

    [Fact]
    public void Minimal_PreservesDatasetIdentifiers()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        Assert.Equal("S421.TST.GMINI.00001", plan.DatasetIdentifier);
        Assert.Equal("S-421", plan.ProductIdentifier);
    }

    [Fact]
    public void Minimal_ProjectsRouteIdentity()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        Assert.Equal("RTE", plan.Route.Id);
        Assert.Equal("1.0", plan.Route.FormatVersion);
        Assert.Equal("GMINI.00001", plan.Route.RouteId);
        Assert.Equal(1, plan.Route.EditionNumber);
    }

    [Fact]
    public void Minimal_ProjectsRouteInfoCoreFields()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        Assert.NotNull(plan.Route.Info);
        Assert.Equal("RTE.INFO", plan.Route.Info!.Id);
        Assert.Equal("Basic.Implementation", plan.Route.Info.Name);
        Assert.Equal("Mikael", plan.Route.Info.Author);
        Assert.Equal(1, plan.Route.Info.Status);
    }

    [Fact]
    public void Minimal_ParsesRouteInfoDateTimesAsUtc()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        var info = plan.Route.Info!;
        Assert.Equal(new DateTimeOffset(2019, 10, 18, 12, 49, 0, TimeSpan.Zero), info.EditionTime);
        Assert.Equal(new DateTimeOffset(2019, 10, 18, 12, 49, 0, TimeSpan.Zero), info.ValidityStart);
        Assert.Equal(new DateTimeOffset(2020, 10, 18, 12, 49, 0, TimeSpan.Zero), info.ValidityEnd);
    }

    [Fact]
    public void Minimal_ProjectsVesselInfoWhenOnlyMmsiPresent()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        var vessel = plan.Route.Info!.Vessel;
        Assert.NotNull(vessel);
        Assert.Equal("265425000", vessel!.Mmsi);
        Assert.Null(vessel.Name);
        Assert.Null(vessel.Imo);
    }

    [Fact]
    public void Minimal_ResolvesWaypointsInDocumentOrder()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        Assert.Equal(2, plan.Route.Waypoints.Length);

        var wp1 = plan.Route.Waypoints[0];
        Assert.Equal("RTE.WPT.1", wp1.Id);
        Assert.Equal(1, wp1.WaypointNumber);
        Assert.Equal("WP Name 1", wp1.Name);
        Assert.True(wp1.Fixed);
        Assert.Equal(0.7, wp1.TurnRadius);
        Assert.Equal(59.892863, wp1.Position.Latitude, 6);
        Assert.Equal(25.822235, wp1.Position.Longitude, 6);

        var wp10 = plan.Route.Waypoints[1];
        Assert.Equal("RTE.WPT.10", wp10.Id);
        Assert.Equal(10, wp10.WaypointNumber);
        Assert.False(wp10.Fixed);
        Assert.Equal("GSN1.123456", wp10.ExternalReferenceId);
    }

    [Fact]
    public void Minimal_HasNoLegsAndNoActionPointsAndNoSchedules()
    {
        var plan = Project("RTE-TEST-GMIN.s421.gml", out _);
        Assert.Empty(plan.Route.Legs);
        Assert.Empty(plan.Route.ActionPoints);
        Assert.Empty(plan.Route.Schedules);
    }

    [Fact]
    public void Minimal_ProducesNoDiagnostics()
    {
        Project("RTE-TEST-GMIN.s421.gml", out var diagnostics);
        Assert.Empty(diagnostics);
    }

    // ── Rich projection (GFULL) ──────────────────────────────────

    [Fact]
    public void Full_ProjectsRouteInfoVesselBlock()
    {
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var vessel = plan.Route.Info!.Vessel!;
        Assert.Equal("BALTIC BRIGHT", vessel.Name);
        Assert.Equal("265425000", vessel.Mmsi);
        Assert.Equal("SIHZ", vessel.Callsign);
        Assert.Equal("9129263", vessel.Imo);
        Assert.Equal(77, vessel.VesselType);
        Assert.Equal(25.0, vessel.HeightMeters);
        Assert.Equal(134.4, vessel.LengthMeters);
        Assert.Equal(20.0, vessel.BeamMeters);
    }

    [Fact]
    public void Full_ProjectsRouteInfoPortsAndReferences()
    {
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var info = plan.Route.Info!;
        Assert.Equal("Sesto", info.DeparturePortId1);
        Assert.Equal("Svartklubben", info.DeparturePortId2);
        Assert.StartsWith("urn:mrn:portcallid:sesto:", info.DeparturePortCall);
        Assert.Equal("Nobgo", info.ArrivalPortId1);
        Assert.Equal("route.id.1", info.PreviousRouteReference);
        Assert.Equal("route.id.3", info.NextRouteReference);
    }

    [Fact]
    public void Full_ProjectsAtLeastOneActionPointAsPoint()
    {
        // The IEC GFULL sample has three RouteActionPoint features (Point,
        // Curve, Surface) but the RouteActionPoints container only references
        // "#RTE.APT.1" — the others are commented out. The typed projection
        // should therefore include exactly the referenced point.
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var ap = Assert.Single(plan.Route.ActionPoints);
        Assert.Equal("RTE.APT.1", ap.Id);
        Assert.Equal(1, ap.ActionPointNumber);
        Assert.Equal("Radio", ap.Name);
        Assert.Equal(1.0, ap.RadiusNauticalMiles);
        Assert.Equal(8.1, ap.TimeToActMinutes);
        Assert.Equal(1, ap.RequiredAction);
        Assert.Equal("Change radio channel", ap.RequiredActionDescription);
        Assert.Equal(S421ActionPointGeometryKind.Point, ap.GeometryKind);
        Assert.Single(ap.Coordinates);
    }

    [Fact]
    public void Full_ProjectsAllThreeScheduleVariants()
    {
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var schedule = Assert.Single(plan.Route.Schedules);
        Assert.Equal("RTE.SCHED.1", schedule.Id);
        Assert.Equal(1, schedule.ScheduleNumber);
        Assert.Equal("Excel", schedule.Name);

        Assert.Equal(3, schedule.Variants.Length);
        var kinds = schedule.Variants.Select(v => v.Kind).ToArray();
        Assert.Contains(S421ScheduleVariantKind.Manual, kinds);
        Assert.Contains(S421ScheduleVariantKind.Calculated, kinds);
        Assert.Contains(S421ScheduleVariantKind.Recommended, kinds);
    }

    [Fact]
    public void Full_ProjectsScheduleElementTimes()
    {
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var manual = plan.Route.Schedules[0].Variants
            .Single(v => v.Kind == S421ScheduleVariantKind.Manual);

        var element = Assert.Single(manual.Elements);
        Assert.Equal(1, element.WaypointNumber);
        Assert.Equal(20.0, element.PlannedSpeedOverGround);
        Assert.Equal(new DateTimeOffset(2019, 10, 18, 15, 0, 0, TimeSpan.Zero), element.Etd);
        Assert.Equal(30, element.EtdWindowBeforeMinutes);
        Assert.Equal(30, element.EtdWindowAfterMinutes);
        Assert.Equal(60, element.EtaWindowBeforeMinutes);
        Assert.Equal(60, element.EtaWindowAfterMinutes);
        Assert.Equal("Set by operator", element.Note);
    }

    // ── Tolerance and diagnostics ────────────────────────────────

    [Fact]
    public void Full_RecordsDiagnosticsForMismatchedWaypointReferences()
    {
        // The IEC GFULL fixture has inconsistent gml:ids: RouteWaypoints
        // references "#RTE.WPTS.WPT.1".."#RTE.WPTS.WPT.10" but only
        // "RTE.WPTS.WPT.7" actually exists as a feature id (the others are
        // "RTE.WPT.1".."RTE.WPT.6"). The projection must surface this as
        // diagnostics rather than throw.
        Project("RTE-TEST-GFULL.s421.gml", out var diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("routeWaypoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void From_NullDataset_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            S421RoutePlan.From(null!, out _));
    }

    [Fact]
    public void From_DatasetWithoutRoute_Throws()
    {
        // Project a synthetic dataset that has no Route feature.
        var empty = new S421Dataset
        {
            ProductIdentifier = "S-421",
            DatasetIdentifier = "test",
            Features = ImmutableArray<S421Feature>.Empty,
            InformationTypes = ImmutableArray<S421InformationType>.Empty,
        };
        Assert.Throws<InvalidOperationException>(() =>
            S421RoutePlan.From(empty, out _));
    }

    [Fact]
    public void RouteInfo_PreservesUnknownAttributesInExtraAttributes()
    {
        // RouteInfoVesselName is a known field, but the spec defines a number
        // of attributes that may not all be modelled. Confirm that any RouteInfo
        // attributes outside the known set round-trip via ExtraAttributes
        // by checking that the routeInfoDraftMax / routeInfoBeamMax / etc.
        // values from GFULL show up there.
        var plan = Project("RTE-TEST-GFULL.s421.gml", out _);
        var extra = plan.Route.Info!.ExtraAttributes;
        Assert.True(extra.ContainsKey("routeInfoDraftMax"));
        Assert.Equal("1000.0", extra["routeInfoDraftMax"]);
    }

    // ── Cross-reference resolution: Route → WP → Leg → endpoint WP ──

    /// <summary>
    /// Builds a synthetic, fully self-consistent S-421 dataset with
    /// <paramref name="waypointCount"/> waypoints and (waypointCount - 1)
    /// legs, so the typed-model bidirectional endpoint linking can be
    /// exercised on a fixture whose <c>gml:id</c>s actually resolve. The
    /// IEC sample fixtures (GFULL/GBASIC) have intentional id mismatches
    /// that make most waypoints unresolvable and so cannot drive these
    /// tests on their own.
    /// </summary>
    private static S421Dataset BuildSyntheticRoute(int waypointCount, bool referenceLegBeyondLastWaypoint = false)
    {
        if (waypointCount < 2)
            throw new ArgumentOutOfRangeException(nameof(waypointCount));

        var emptyAttrs = ImmutableDictionary<string, string>.Empty;
        var emptyComplex = ImmutableArray<S421ComplexAttribute>.Empty;

        var features = ImmutableArray.CreateBuilder<S421Feature>();

        // Route → routeWaypoints container.
        features.Add(new S421Feature
        {
            Id = "RTE",
            FeatureType = "Route",
            GeometryType = GmlGeometryType.None,
            Points = ImmutableArray<(double, double)>.Empty,
            Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            ExteriorRing = ImmutableArray<(double, double)>.Empty,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = emptyAttrs,
            ComplexAttributes = emptyComplex,
            References = ImmutableArray.Create(new GmlReference
            {
                Role = "routeWaypoints",
                Href = "#RTE.WPTS",
            }),
        });

        // RouteWaypoints container with routeWaypoint xlinks.
        var containerRefs = ImmutableArray.CreateBuilder<GmlReference>();
        for (int i = 1; i <= waypointCount; i++)
        {
            containerRefs.Add(new GmlReference { Role = "routeWaypoint", Href = $"#RTE.WPT.{i}" });
        }
        features.Add(new S421Feature
        {
            Id = "RTE.WPTS",
            FeatureType = "RouteWaypoints",
            GeometryType = GmlGeometryType.None,
            Points = ImmutableArray<(double, double)>.Empty,
            Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            ExteriorRing = ImmutableArray<(double, double)>.Empty,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = emptyAttrs,
            ComplexAttributes = emptyComplex,
            References = containerRefs.ToImmutable(),
        });

        // Waypoints + legs. Each waypoint except the last gets an outgoing
        // leg; the legs themselves are added as separate features.
        int legCount = waypointCount - 1 + (referenceLegBeyondLastWaypoint ? 1 : 0);
        for (int i = 1; i <= waypointCount; i++)
        {
            var refs = ImmutableArray.CreateBuilder<GmlReference>();
            bool hasOutgoing = i < waypointCount || referenceLegBeyondLastWaypoint;
            if (hasOutgoing)
            {
                refs.Add(new GmlReference
                {
                    Role = "routeWaypointLeg",
                    Href = $"#RTE.WPT.LEG.{i}",
                });
            }

            features.Add(new S421Feature
            {
                Id = $"RTE.WPT.{i}",
                FeatureType = "RouteWaypoint",
                GeometryType = GmlGeometryType.Point,
                Points = ImmutableArray.Create((60.0 + 0.01 * i, 25.0 + 0.01 * i)),
                Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
                ExteriorRing = ImmutableArray<(double, double)>.Empty,
                InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
                Attributes = ImmutableDictionary<string, string>.Empty.Add("routeWaypointID", i.ToString()),
                ComplexAttributes = emptyComplex,
                References = refs.ToImmutable(),
            });
        }

        for (int i = 1; i <= legCount; i++)
        {
            features.Add(new S421Feature
            {
                Id = $"RTE.WPT.LEG.{i}",
                FeatureType = "RouteWaypointLeg",
                GeometryType = GmlGeometryType.Curve,
                Points = ImmutableArray<(double, double)>.Empty,
                Curves = ImmutableArray.Create(
                    ImmutableArray.Create(
                        (60.0 + 0.01 * i, 25.0 + 0.01 * i),
                        (60.0 + 0.01 * (i + 1), 25.0 + 0.01 * (i + 1)))),
                ExteriorRing = ImmutableArray<(double, double)>.Empty,
                InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
                Attributes = emptyAttrs,
                ComplexAttributes = emptyComplex,
                References = ImmutableArray<GmlReference>.Empty,
            });
        }

        return new S421Dataset
        {
            ProductIdentifier = "S-421",
            DatasetIdentifier = "synthetic",
            Features = features.ToImmutable(),
            InformationTypes = ImmutableArray<S421InformationType>.Empty,
        };
    }

    [Fact]
    public void Synthetic_LegStartWaypointMatchesOriginatingWaypoint()
    {
        var plan = S421RoutePlan.From(BuildSyntheticRoute(4), out _);
        Assert.Equal(4, plan.Route.Waypoints.Length);
        Assert.Equal(3, plan.Route.Legs.Length);

        for (int i = 0; i < plan.Route.Waypoints.Length - 1; i++)
        {
            var wp = plan.Route.Waypoints[i];
            Assert.NotNull(wp.OutgoingLeg);
            Assert.Same(wp, wp.OutgoingLeg!.StartWaypoint);
        }
    }

    [Fact]
    public void Synthetic_LegEndWaypointMatchesNextWaypoint()
    {
        var plan = S421RoutePlan.From(BuildSyntheticRoute(4), out _);

        for (int i = 0; i < plan.Route.Waypoints.Length - 1; i++)
        {
            var wp = plan.Route.Waypoints[i];
            var next = plan.Route.Waypoints[i + 1];
            Assert.Same(next, wp.OutgoingLeg!.EndWaypoint);
        }
    }

    [Fact]
    public void Synthetic_IncomingLegMirrorsPreviousOutgoingLeg()
    {
        var plan = S421RoutePlan.From(BuildSyntheticRoute(4), out _);

        // First waypoint has no incoming leg.
        Assert.Null(plan.Route.Waypoints[0].IncomingLeg);

        for (int i = 1; i < plan.Route.Waypoints.Length; i++)
        {
            var prev = plan.Route.Waypoints[i - 1];
            var current = plan.Route.Waypoints[i];
            Assert.NotNull(current.IncomingLeg);
            Assert.Same(prev.OutgoingLeg, current.IncomingLeg);
        }
    }

    [Fact]
    public void Synthetic_LastWaypointHasNoOutgoingLeg()
    {
        var plan = S421RoutePlan.From(BuildSyntheticRoute(4), out _);
        var last = plan.Route.Waypoints[^1];
        Assert.Null(last.OutgoingLeg);
    }

    [Fact]
    public void Synthetic_FullWalkFromRouteThroughLegsBackToWaypoints()
    {
        // End-to-end traversal: starting at plan.Route, walk forward via
        // wp.OutgoingLeg.EndWaypoint and collect waypoint ids; the result
        // must match plan.Route.Waypoints in order.
        var plan = S421RoutePlan.From(BuildSyntheticRoute(5), out var diagnostics);
        Assert.Empty(diagnostics);

        var walked = new List<string>();
        var cursor = plan.Route.Waypoints[0];
        walked.Add(cursor.Id);
        while (cursor.OutgoingLeg is { } leg)
        {
            Assert.Same(cursor, leg.StartWaypoint);
            Assert.NotNull(leg.EndWaypoint);
            cursor = leg.EndWaypoint!;
            walked.Add(cursor.Id);
        }

        Assert.Equal(
            plan.Route.Waypoints.Select(w => w.Id).ToArray(),
            walked.ToArray());
    }

    [Fact]
    public void Synthetic_EmitsDiagnosticWhenTerminalWaypointHasOutgoingLeg()
    {
        // Build a dataset where the LAST waypoint also references a leg —
        // the projection should flag this as route.leg.endpoint.missing
        // (there is no successor waypoint to terminate at), not throw.
        var plan = S421RoutePlan.From(
            BuildSyntheticRoute(3, referenceLegBeyondLastWaypoint: true),
            out var diagnostics);

        var terminalLeg = plan.Route.Waypoints[^1].OutgoingLeg;
        Assert.NotNull(terminalLeg);
        Assert.Null(terminalLeg!.EndWaypoint);

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Code == "route.leg.endpoint.missing");
    }

    [Fact]
    public void DiagnosticToString_IncludesSeverityAndMessage()
    {
        var d = new ProjectionDiagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = "X",
            RelatedId = "ID1",
        };
        Assert.Contains("Warning", d.ToString());
        Assert.Contains("ID1", d.ToString());
        Assert.Contains("X", d.ToString());
    }
}
