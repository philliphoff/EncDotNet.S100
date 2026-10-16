using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>Tests for <see cref="S421DatasetWriter"/> (#390).</summary>
public class S421DatasetWriterTests
{
    private const string TestDataDir = "TestData";

    [Theory]
    [InlineData("RTE-TEST-GMIN.s421.gml")]
    [InlineData("RTE-TEST-GBASIC.s421.gml")]
    [InlineData("RTE-TEST-GFULL.s421.gml")]
    public void Read_write_read_keeps_the_typed_route(string fileName)
    {
        var original = S421RoutePlan.From(LoadRepaired(fileName), out var originalDiagnostics);
        Assert.True(original.Route.Waypoints.Count >= 2);

        var bytes = S421DatasetWriter.ToBytes(original.Route);
        var reread = S421RoutePlan.From(S421Dataset.Open(new MemoryStream(bytes)), out var diagnostics);

        Assert.Equal(Describe(original.Route), Describe(reread.Route));
        Assert.Equal(originalDiagnostics.Count, diagnostics.Count);
    }

    [Fact]
    public void Writing_the_same_route_twice_gives_the_same_bytes()
    {
        var route = SyntheticRoute();

        Assert.Equal(S421DatasetWriter.ToBytes(route), S421DatasetWriter.ToBytes(route));
    }

    [Fact]
    public void A_synthetic_route_reads_back_with_its_values()
    {
        var route = SyntheticRoute();

        var dataset = S421Dataset.Open(new MemoryStream(S421DatasetWriter.ToBytes(route, new S421WriteOptions { DatasetIdentifier = "S421.ZZ00.TEST" })));
        var plan = S421RoutePlan.From(dataset, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal("S421.ZZ00.TEST", dataset.DatasetIdentifier);
        Assert.Equal(Describe(route), Describe(plan.Route));
        Assert.Empty(S421RoutePlanRules.Validate(plan).Findings);
    }

    [Fact]
    public void Positions_are_written_latitude_first()
    {
        var route = SyntheticRoute();

        var text = Encoding.UTF8.GetString(S421DatasetWriter.ToBytes(route));

        Assert.Contains("<gml:pos>-41.02 -134.98</gml:pos>", text, StringComparison.Ordinal);
        Assert.Contains("<gml:posList>-41.02 -134.98 -41.01 -134.97</gml:posList>", text, StringComparison.Ordinal);
        Assert.Equal(AxisOrderOutcome.AsEncoded, S421Dataset.Open(new MemoryStream(Encoding.UTF8.GetBytes(text))).AxisOrder?.Outcome);
    }

    [Fact]
    public void Each_leg_is_referenced_from_the_waypoint_it_ends_at()
    {
        var route = SyntheticRoute();

        var dataset = S421Dataset.Open(new MemoryStream(S421DatasetWriter.ToBytes(route)));

        var waypoints = dataset.Features.Where(f => f.FeatureType == "RouteWaypoint").ToList();
        Assert.DoesNotContain(waypoints[0].References, r => r.Role == "routeWaypointLeg");
        Assert.Equal("#RTE.LEG.1", Assert.Single(waypoints[1].References, r => r.Role == "routeWaypointLeg").Href);
        Assert.Equal("#RTE.LEG.2", Assert.Single(waypoints[2].References, r => r.Role == "routeWaypointLeg").Href);
    }

    [Fact]
    public void The_IEC_sample_legs_run_from_the_waypoint_before_the_one_that_references_them()
    {
        // RTE.WPT.2 references RTE.WPT.LEG.1, whose curve runs from WPT.1 to WPT.2.
        var plan = S421RoutePlan.From(LoadRepaired("RTE-TEST-GBASIC.s421.gml"), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(10, plan.Route.Waypoints.Count);
        Assert.Equal(9, plan.Route.Legs.Count);

        var first = plan.Route.Waypoints[0];
        var leg = Assert.IsType<S421Leg>(first.OutgoingLeg);
        Assert.Equal("RTE.WPT.LEG.1", leg.Id);
        Assert.Same(first, leg.StartWaypoint);
        Assert.Same(plan.Route.Waypoints[1], leg.EndWaypoint);
        Assert.Equal(first.Position, leg.Coordinates[0]);
        Assert.Equal(plan.Route.Waypoints[1].Position, leg.Coordinates[^1]);
        Assert.Null(plan.Route.Waypoints[^1].OutgoingLeg);
        Assert.Null(first.IncomingLeg);
        Assert.Same(leg, plan.Route.Waypoints[1].IncomingLeg);
    }

    [Fact]
    public void A_written_route_links_incoming_legs()
    {
        var plan = S421RoutePlan.From(S421Dataset.Open(new MemoryStream(S421DatasetWriter.ToBytes(SyntheticRoute()))), out _);

        Assert.Null(plan.Route.Waypoints[0].IncomingLeg);
        Assert.Equal("RTE.LEG.1", plan.Route.Waypoints[1].IncomingLeg?.Id);
        Assert.Equal("RTE.LEG.2", plan.Route.Waypoints[2].IncomingLeg?.Id);
        Assert.All(plan.Route.Legs.Select((leg, i) => (leg, i)), x =>
        {
            Assert.Same(plan.Route.Waypoints[x.i], x.leg.StartWaypoint);
            Assert.Same(plan.Route.Waypoints[x.i + 1], x.leg.EndWaypoint);
        });
    }

    /// <summary>
    /// Opens an IEC sample with its <c>RouteWaypoints</c> references repaired:
    /// the samples refer to <c>#RTE.WPTS.WP.n</c> or <c>#RTE.WPTS.WPT.n</c>,
    /// but the waypoints are <c>RTE.WPT.n</c>, so as published only the
    /// minimal sample resolves its waypoints.
    /// </summary>
    private static S421Dataset LoadRepaired(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(TestDataDir, fileName))
            .Replace("RTE.WPTS.WPT.", "RTE.WPT.", StringComparison.Ordinal)
            .Replace("RTE.WPTS.WP.", "RTE.WPT.", StringComparison.Ordinal);
        return S421Dataset.Open(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void A_route_needs_two_waypoints()
    {
        var route = SyntheticRoute();
        var single = new S421Route
        {
            Id = route.Id,
            Waypoints = [route.Waypoints[0]],
            Legs = [],
            ActionPoints = [],
            Schedules = [],
            ExtraAttributes = new Dictionary<string, string>(),
        };

        Assert.Throws<ArgumentException>(() => S421DatasetWriter.ToBytes(single));
    }

    private static S421Route SyntheticRoute()
    {
        var empty = new Dictionary<string, string>();
        S421Leg Leg(int n, GeoPosition from, GeoPosition to) => new()
        {
            Id = $"RTE.LEG.{n}",
            Coordinates = [from, to],
            StarboardCrossTrackDistanceLimit = 100,
            PortCrossTrackDistanceLimit = 120.5,
            GeometryTypeCode = 1,
            Draft = 5.5,
            StaticUnderKeelClearance = 1.0,
            SpeedOverGroundMax = 9,
            Note = $"Leg {n} (SYNTHETIC)",
            ExtraAttributes = new Dictionary<string, string> { ["routeWaypointLegDraftForward"] = "5.4" },
        };

        var p1 = new GeoPosition(-41.03, -134.99);
        var p2 = new GeoPosition(-41.02, -134.98);
        var p3 = new GeoPosition(-41.01, -134.97);
        var leg1 = Leg(1, p1, p2);
        var leg2 = Leg(2, p2, p3);
        S421Waypoint Waypoint(int n, GeoPosition position, S421Leg? outgoing) => new()
        {
            Id = $"RTE.WPT.{n}",
            WaypointNumber = n,
            Name = $"WP{n} (fictitious)",
            Fixed = n == 1,
            TurnRadius = 0.1,
            Position = position,
            OutgoingLeg = outgoing,
            ExtraAttributes = empty,
        };

        var departure = new DateTimeOffset(2026, 3, 1, 6, 30, 0, TimeSpan.Zero);
        return new S421Route
        {
            Id = "RTE",
            FormatVersion = "1.0",
            RouteId = "ZZ00-SYNTHETIC-1",
            EditionNumber = 1,
            Info = new S421RouteInfo
            {
                Id = "RTE.INFO",
                Name = "Synthetic approach (fictitious)",
                Author = "Synthetic Hydrographic Office (fictitious)",
                Status = 1,
                ValidityStart = departure,
                Vessel = new S421VesselInfo { Name = "MV SYNTHETIC (fictitious)", LengthMeters = 120, BeamMeters = 20 },
                ExtraAttributes = new Dictionary<string, string> { ["routeInfoDraftMax"] = "6.5" },
            },
            Waypoints = [Waypoint(1, p1, leg1), Waypoint(2, p2, leg2), Waypoint(3, p3, null)],
            Legs = [leg1, leg2],
            ActionPoints =
            [
                new S421ActionPoint
                {
                    Id = "RTE.AP.1",
                    ActionPointNumber = 1,
                    Name = "Call pilot (fictitious)",
                    RequiredAction = 1,
                    GeometryKind = S421ActionPointGeometryKind.Point,
                    Coordinates = [p2],
                    ExtraAttributes = empty,
                },
            ],
            Schedules =
            [
                new S421Schedule
                {
                    Id = "RTE.SCHED.1",
                    ScheduleNumber = 1,
                    Name = "Planned",
                    Variants =
                    [
                        new S421ScheduleVariant
                        {
                            Id = "RTE.SCHED.1.MAN",
                            Kind = S421ScheduleVariantKind.Manual,
                            Elements =
                            [
                                new S421ScheduleElement { Id = "RTE.SCHED.1.MAN.1", WaypointNumber = 1, PlannedSpeedOverGround = 8, Etd = departure, ExtraAttributes = empty },
                                new S421ScheduleElement { Id = "RTE.SCHED.1.MAN.2", WaypointNumber = 2, PlannedSpeedOverGround = 6, ExtraAttributes = empty },
                            ],
                        },
                    ],
                    ExtraAttributes = empty,
                },
            ],
            ExtraAttributes = empty,
        };
    }

    // Every value of the typed model, in a canonical text form.
    private static string Describe(S421Route route)
    {
        var text = new StringBuilder();
        void Line(string name, object? value) => text.Append(name).Append('=').Append(Format(value)).Append('\n');
        void Extras(IReadOnlyDictionary<string, string> extras)
        {
            foreach (var (key, value) in extras.OrderBy(e => e.Key, StringComparer.Ordinal))
                Line("  +" + key, value);
        }

        Line("route", (route.Id, route.FormatVersion, route.RouteId, route.EditionNumber));
        Extras(route.ExtraAttributes);
        if (route.Info is { } i)
        {
            Line("info", (i.Id, i.Name, i.Author, i.Description, i.Status, i.EditionTime, i.ValidityStart, i.ValidityEnd));
            Line("ports", (i.DeparturePortId1, i.DeparturePortId2, i.DeparturePortCall, i.ArrivalPortId1, i.ArrivalPortId2, i.ArrivalPortCall, i.PreviousRouteReference, i.NextRouteReference));
            if (i.Vessel is { } v)
                Line("vessel", (v.VesselType, v.Name, v.Mmsi, v.Callsign, v.Imo, v.VoyageId, v.HeightMeters, v.LengthMeters, v.BeamMeters));
            Extras(i.ExtraAttributes);
        }

        foreach (var w in route.Waypoints)
        {
            Line("waypoint", (w.Id, w.WaypointNumber, w.Name, w.ExternalReferenceId, w.Fixed, w.TurnRadius, w.Position.Latitude, w.Position.Longitude, w.OutgoingLeg?.Id));
            Extras(w.ExtraAttributes);
        }

        foreach (var l in route.Legs)
        {
            Line("leg", (l.Id, string.Join(' ', l.Coordinates.Select(c => Format(c.Latitude) + "," + Format(c.Longitude)))));
            Line("  limits", (l.StarboardCrossTrackDistanceLimit, l.PortCrossTrackDistanceLimit, l.StarboardChannelLimit, l.PortChannelLimit, l.SafetyContour, l.SafetyDepth, l.GeometryTypeCode));
            Line("  speeds", (l.SpeedOverGroundMin, l.SpeedOverGroundMax, l.SpeedThroughWaterMin, l.SpeedThroughWaterMax));
            Line("  vessel", (l.Draft, l.StaticUnderKeelClearance, l.DynamicUnderKeelClearance, l.SafetyMargin, l.Note));
            Extras(l.ExtraAttributes);
        }

        foreach (var a in route.ActionPoints)
        {
            Line("action", (a.Id, a.ActionPointNumber, a.Name, a.RadiusNauticalMiles, a.TimeToActMinutes, a.RequiredAction, a.RequiredActionDescription, a.GeometryKind, string.Join(' ', a.Coordinates.Select(c => Format(c.Latitude) + "," + Format(c.Longitude)))));
            Extras(a.ExtraAttributes);
        }

        foreach (var s in route.Schedules)
        {
            Line("schedule", (s.Id, s.ScheduleNumber, s.Name));
            Extras(s.ExtraAttributes);
            foreach (var variant in s.Variants)
            {
                Line("  variant", (variant.Id, variant.Kind));
                foreach (var e in variant.Elements)
                {
                    Line("    element", (e.Id, e.WaypointNumber, e.PlannedSpeedOverGround, e.Etd, e.Eta, e.EtdWindowBeforeMinutes, e.EtdWindowAfterMinutes, e.EtaWindowBeforeMinutes, e.EtaWindowAfterMinutes, e.Note));
                    Extras(e.ExtraAttributes);
                }
            }
        }

        return text.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        ITuple t => "(" + string.Join(", ", Enumerable.Range(0, t.Length).Select(i => Format(t[i]))) + ")",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
