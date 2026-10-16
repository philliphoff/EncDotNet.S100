using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Warnings on route": the expected answer for a route check against
/// S-124 (#888), and the same answer worked out from the files through the
/// S-421 and S-124 readers.
/// </summary>
public abstract class WarningsOnRouteTests(GeneratedScenarioFixture fixture, int edition)
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private S421RouteSpec Route => Definition.Routes[0];

    [Fact]
    public void The_truth_is_the_documented_table()
    {
        var truth = RouteWarnings.For(Definition, Route).ToDictionary(t => t.WarningNumber);

        Assert.Equal((WarningRouteRelation.Crosses, true), (truth[1].Relation, truth[1].InForce));
        Assert.Equal((WarningRouteRelation.Crosses, false), (truth[2].Relation, truth[2].InForce));
        Assert.Equal((WarningRouteRelation.Near, true), (truth[3].Relation, truth[3].InForce));
        Assert.Equal(WarningRouteRelation.Off, truth[4].Relation);
        Assert.Equal((WarningRouteRelation.Crosses, false), (truth[5].Relation, truth[5].InForce));
        Assert.Equal(WarningRouteRelation.Off, truth[6].Relation);

        // Edition 1.0.0 has no cancellation date, so the tow stays in force.
        Assert.Equal((WarningRouteRelation.Crosses, edition == 1), (truth[7].Relation, truth[7].InForce));

        // The survey area spans 2400–2800 m east, entered 2100 m from departure.
        Assert.Equal(2100, truth[1].StartDistanceMeters, 6);
        Assert.Equal(2500, truth[1].EndDistanceMeters, 6);
        Assert.Equal(new DateTime(2026, 3, 1, 6, 8, 30, DateTimeKind.Utc), truth[1].StartTime);
    }

    [Fact]
    public void The_files_give_the_same_answer_as_the_model()
    {
        var truth = RouteWarnings.For(Definition, Route);
        var legs = ScenarioPassage.Legs(_catalog, Route);
        var schedule = PassageSchedule.Compute(legs, new DateTimeOffset(Route.Departure, TimeSpan.Zero));
        var frame = Definition.World.Frame;
        var path = legs.Select(l => l.Points[0]).Append(legs[^1].Points[^1]).Select(p => frame.ToLocal(p.Latitude, p.Longitude)).ToList();
        var warnings = Definition.Warnings.ToDictionary(w => w.WarningNumber, Read);

        foreach (var expected in truth)
        {
            var warning = warnings[expected.WarningNumber];
            var part = Assert.Single(warning.Parts);
            var kind = part.GeometryKind switch
            {
                S124GeometryKind.Point => WarningGeometryKind.Point,
                S124GeometryKind.Curve => WarningGeometryKind.Line,
                _ => WarningGeometryKind.Area,
            };
            var shape = part.Coordinates.Select(p => frame.ToLocal(p.Latitude, p.Longitude)).ToList();
            if (kind == WarningGeometryKind.Area)
            {
                shape.RemoveAt(shape.Count - 1);
            }

            var (relation, start, end) = RouteWarnings.Relate(path, kind, shape);
            Assert.Equal(expected.Relation, relation);
            if (relation == WarningRouteRelation.Off)
            {
                continue;
            }

            Assert.Equal(expected.StartDistanceMeters, start, 0.05);
            Assert.Equal(expected.EndDistanceMeters, end, 0.05);

            // ETAs from the S-421 legs (geodesic) agree with the model's clock to a couple of seconds.
            var startEta = Eta(schedule, path, start);
            var endEta = Eta(schedule, path, end);
            Assert.InRange((startEta.UtcDateTime - expected.StartTime!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));

            Assert.Equal(expected.InForce, InForce(warning, warnings.Values, startEta, endEta));
        }
    }

    private S124NavigationalWarning Read(S124WarningSpec spec)
    {
        var dataset = Assert.IsType<S124DatasetData>(ScenarioLoading.Dataset(_catalog, spec.DatasetName).Data).Model;
        return S124NavigationalWarning.From(dataset, out _);
    }

    // The ETA at a distance along the route: the same fraction of the leg on the S-421 geometry.
    private static DateTimeOffset Eta(PassageSchedule schedule, IReadOnlyList<(double X, double Y)> path, double distance)
    {
        for (var i = 1; i < path.Count; i++)
        {
            var length = Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].Y - path[i - 1].Y, 2));
            if (distance <= length || i == path.Count - 1)
            {
                return schedule.EtaAt(i - 1, distance / length * schedule.Legs[i - 1].DistanceMeters)!.Value;
            }

            distance -= length;
        }

        throw new InvalidOperationException("Empty route.");
    }

    // In force while the vessel is there, from what the files say: validity,
    // the cancellation date, and cancellations by other warnings.
    private static bool InForce(S124NavigationalWarning warning, IEnumerable<S124NavigationalWarning> all, DateTimeOffset start, DateTimeOffset end)
    {
        var part = warning.Parts[0];
        if (!part.ValidityRanges.Any(r => (r.Start is null || r.Start <= end) && (r.End is null || r.End >= start)))
        {
            return false;
        }

        if (warning.Preamble?.CancellationDate is { } cancelled && cancelled <= start)
        {
            return false;
        }

        var msi = warning.Preamble!.MessageSeriesIdentifier!;
        var reference = S124WarningWriter.MessageReference(msi.WarningNumber!.Value, msi.Year!.Value);
        return !all.Any(other => other.References.Any(r =>
            r.ReferenceCategory == 1
            && (r.Warnings.Any(w => w.WarningNumber == msi.WarningNumber && w.Year == msi.Year) || r.MessageReference == reference)
            && (other.Preamble?.PublicationTime is not { } published || published <= start)));
    }
}

public sealed class WarningsOnRouteEdition2Tests(WarningsOnRouteFixture fixture)
    : WarningsOnRouteTests(fixture, edition: 2), IClassFixture<WarningsOnRouteFixture>;

public sealed class WarningsOnRouteEdition1Tests(WarningsOnRouteEdition1Fixture fixture)
    : WarningsOnRouteTests(fixture, edition: 1), IClassFixture<WarningsOnRouteEdition1Fixture>;
