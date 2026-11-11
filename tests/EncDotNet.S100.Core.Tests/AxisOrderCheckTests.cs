using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for the shared dataset-level axis-order check (issue #650).
/// </summary>
public class AxisOrderCheckTests
{
    // UKHO S-104 Solent trial: declared extent lat 50..51, lon −2.5..0.
    private static readonly BoundingBox Solent = new(50.0, -2.5, 51.0, 0.0);

    private static readonly GeoPosition[] SolentStations =
    [
        new(50.5667, -2.4667),
        new(50.6167, -2.25),
        new(50.7667, -1.3),
        new(50.9167, -0.1),
    ];

    private static GeoPosition[] Swapped(IEnumerable<GeoPosition> positions) =>
        positions.Select(AxisOrderCheck.Swap).ToArray();

    [Fact]
    public void Positions_fitting_the_declared_extent_are_verified()
    {
        var a = AxisOrderCheck.Assess(SolentStations, Solent)!;

        Assert.Equal(AxisOrderOutcome.AsEncoded, a.Outcome);
        Assert.Equal(AxisOrderEvidence.DeclaredExtent, a.Evidence);
        Assert.Equal(4, a.FitAsEncoded);
        Assert.False(a.IsNotice);
        Assert.False(a.Normalized);
    }

    [Fact]
    public void Positions_fitting_the_declared_extent_only_swapped_are_swapped()
    {
        // The dcf8 Solent file: "latitude" −2.47..−0.1, "longitude" 50.17..50.92.
        var a = AxisOrderCheck.Assess(Swapped(SolentStations), Solent, "/Positioning/geometryValues")!;

        Assert.Equal(AxisOrderOutcome.Swapped, a.Outcome);
        Assert.Equal(AxisOrderEvidence.DeclaredExtent, a.Evidence);
        Assert.Equal(0, a.FitAsEncoded);
        Assert.Equal(4, a.FitSwapped);
        Assert.True(a.Normalized);
        Assert.True(a.IsWarning);
        Assert.Contains("/Positioning/geometryValues", a.BuildMessage(), StringComparison.Ordinal);
        Assert.Contains("swapped for the whole dataset", a.BuildMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_only_caller_is_not_marked_normalized()
    {
        var a = AxisOrderCheck.Assess(Swapped(SolentStations), Solent, normalizes: false)!;

        Assert.Equal(AxisOrderOutcome.Swapped, a.Outcome);
        Assert.False(a.Normalized);
        Assert.Contains("were not swapped", a.BuildMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tight_ordinal_envelope_that_fits_only_swapped_positions_is_trusted()
    {
        // The UK S-122 trial: a computed lat-lon envelope around lon-lat data.
        var envelope = new BoundingBox(50.2, -3.0, 51.0, 0.0);
        GeoPosition[] encoded = [new(-2.5, 50.95), new(-2.5, 50.3), new(0.0, 50.3), new(0.0, 50.95)];

        var a = AxisOrderCheck.Assess(encoded, envelope, extentIsOrdinal: true)!;

        Assert.Equal(AxisOrderOutcome.Swapped, a.Outcome);
        Assert.False(a.ExtentMayBeSwapped);
    }

    [Fact]
    public void A_loose_ordinal_envelope_that_fits_only_swapped_positions_is_not_trusted()
    {
        // The IEC S-421 test routes: a hand-written lon-lat "Europe" envelope
        // (−6..45, 40..65) around lat-lon Baltic waypoints.
        var envelope = new BoundingBox(-6.0, 40.0, 45.0, 65.0);
        GeoPosition[] encoded = [new(59.89, 25.82), new(57.0, 19.0), new(54.75, 12.69)];

        var a = AxisOrderCheck.Assess(encoded, envelope, extentIsOrdinal: true)!;

        Assert.Equal(AxisOrderOutcome.Inconsistent, a.Outcome);
        Assert.True(a.ExtentMayBeSwapped);
        Assert.True(a.IsWarning);
        Assert.Contains("too loose", a.BuildMessage(), StringComparison.Ordinal);
        Assert.Same(encoded, AxisOrderCheck.Apply(a, encoded));
    }

    [Fact]
    public void A_named_extent_is_trusted_however_loose()
    {
        // HDF5 bounding boxes name their edges, so they can't be swapped.
        var box = new BoundingBox(40.0, -6.0, 65.0, 45.0);
        GeoPosition[] encoded = [new(25.82, 59.89), new(19.0, 57.0), new(12.69, 54.75)];

        Assert.Equal(AxisOrderOutcome.Swapped, AxisOrderCheck.Assess(encoded, box)!.Outcome);
    }

    [Fact]
    public void Latitudes_beyond_90_prove_longitude_first_without_an_extent()
    {
        GeoPosition[] encoded = [new(-150.0, 70.0), new(30.0, 75.0)];

        var a = AxisOrderCheck.Assess(encoded, declaredExtent: null)!;

        Assert.Equal(AxisOrderOutcome.Swapped, a.Outcome);
        Assert.Equal(AxisOrderEvidence.PhysicalBounds, a.Evidence);
        Assert.Equal(1, a.OutOfPhysicalBounds);
    }

    [Fact]
    public void Physical_bounds_win_over_an_extent_written_in_the_same_wrong_order()
    {
        // A lon-first envelope with plausible values cannot outvote a
        // latitude of 150°.
        GeoPosition[] encoded = [new(150.0, 70.0), new(10.0, 75.0)];
        var lonFirstEnvelope = new BoundingBox(0.0, 60.0, 80.0, 90.0);

        var a = AxisOrderCheck.Assess(encoded, lonFirstEnvelope)!;

        Assert.Equal(AxisOrderOutcome.Swapped, a.Outcome);
        Assert.Equal(AxisOrderEvidence.PhysicalBounds, a.Evidence);
    }

    [Fact]
    public void Out_of_bounds_in_both_orders_is_inconsistent()
    {
        GeoPosition[] encoded = [new(150.0, 170.0)];

        var a = AxisOrderCheck.Assess(encoded, declaredExtent: null)!;

        Assert.Equal(AxisOrderOutcome.Inconsistent, a.Outcome);
        Assert.Equal(AxisOrderEvidence.PhysicalBounds, a.Evidence);
    }

    [Fact]
    public void No_extent_and_in_bounds_is_unverified()
    {
        var a = AxisOrderCheck.Assess(SolentStations, declaredExtent: null)!;

        Assert.Equal(AxisOrderOutcome.Unverified, a.Outcome);
        Assert.Equal(AxisOrderEvidence.None, a.Evidence);
        Assert.True(a.IsNotice);
        Assert.False(a.IsWarning);
    }

    [Fact]
    public void Positions_fitting_the_extent_in_neither_order_are_inconsistent()
    {
        GeoPosition[] elsewhere = [new(10.0, 10.0), new(11.0, 11.0)];

        var a = AxisOrderCheck.Assess(elsewhere, Solent)!;

        Assert.Equal(AxisOrderOutcome.Inconsistent, a.Outcome);
        Assert.Equal(AxisOrderEvidence.DeclaredExtent, a.Evidence);
    }

    [Fact]
    public void A_minority_of_stray_positions_does_not_flip_the_decision()
    {
        // 3 of 4 inside as encoded (75%) still verifies.
        GeoPosition[] mostly = [.. SolentStations.Take(3), new(10.0, 10.0)];

        Assert.Equal(AxisOrderOutcome.AsEncoded, AxisOrderCheck.Assess(mostly, Solent)!.Outcome);
    }

    [Fact]
    public void A_zero_filled_extent_is_no_anchor()
    {
        var a = AxisOrderCheck.Assess(Swapped(SolentStations), new BoundingBox(0, 0, 0, 0))!;

        Assert.Equal(AxisOrderOutcome.Unverified, a.Outcome);
        Assert.Null(a.DeclaredExtent);
    }

    [Fact]
    public void An_extent_with_latitudes_beyond_90_is_no_anchor()
    {
        var a = AxisOrderCheck.Assess(SolentStations, new BoundingBox(-2.5, 50.0, 100.0, 51.0))!;

        Assert.Equal(AxisOrderOutcome.Unverified, a.Outcome);
    }

    [Fact]
    public void An_extent_across_the_antimeridian_contains_both_sides()
    {
        var pacific = new BoundingBox(50.0, 170.0, 60.0, -170.0);
        GeoPosition[] positions = [new(55.0, 175.0), new(55.0, -175.0)];

        Assert.Equal(AxisOrderOutcome.AsEncoded, AxisOrderCheck.Assess(positions, pacific)!.Outcome);
    }

    [Fact]
    public void An_extent_with_reversed_west_and_east_edges_is_read_back()
    {
        // IC-ENC Peru S-111: westBoundLongitude −68, eastBoundLongitude −98.
        var reversed = new BoundingBox(-21.0, -68.0, 0.0, -98.0);
        GeoPosition[] positions = [new(-12.0, -77.0), new(-5.0, -81.0)];

        var a = AxisOrderCheck.Assess(positions, reversed)!;

        Assert.Equal(AxisOrderOutcome.AsEncoded, a.Outcome);
        Assert.Equal(-98.0, a.DeclaredExtent!.WestLongitude);
    }

    [Fact]
    public void An_extent_in_0_to_360_contains_negative_longitudes()
    {
        var extent = new BoundingBox(50.0, 180.0, 60.0, 200.0);
        GeoPosition[] positions = [new(55.0, -170.0)];

        Assert.Equal(AxisOrderOutcome.AsEncoded, AxisOrderCheck.Assess(positions, extent)!.Outcome);
    }

    [Fact]
    public void No_positions_gives_no_assessment()
    {
        Assert.Null(AxisOrderCheck.Assess([], Solent));
        Assert.Null(AxisOrderCheck.Assess([new GeoPosition(double.NaN, 1.0)], Solent));
    }

    [Fact]
    public void Apply_swaps_only_a_swapped_outcome()
    {
        var swapped = AxisOrderCheck.Assess(Swapped(SolentStations), Solent);
        var verified = AxisOrderCheck.Assess(SolentStations, Solent);

        Assert.Equal(SolentStations, AxisOrderCheck.Apply(swapped, Swapped(SolentStations)));
        Assert.Same(SolentStations, AxisOrderCheck.Apply(verified, SolentStations));
        Assert.Same(SolentStations, AxisOrderCheck.Apply(null, SolentStations));
    }

    [Fact]
    public void GridCorners_span_origin_to_last_node()
    {
        var corners = AxisOrderCheck.GridCorners(50.0, -1.0, 0.01, 0.02, 11, 6).ToArray();

        Assert.Equal(4, corners.Length);
        Assert.Contains(new GeoPosition(50.0, -1.0), corners);
        Assert.Contains(corners, c => Math.Abs(c.Latitude - 50.1) < 1e-9 && Math.Abs(c.Longitude - -0.9) < 1e-9);
    }

    [Fact]
    public void Findings_report_swaps_as_warnings_and_nothing_for_unverified()
    {
        var swapped = AxisOrderCheck.Assess(Swapped(SolentStations), Solent);
        var unverified = AxisOrderCheck.Assess(SolentStations, null);
        var verified = AxisOrderCheck.Assess(SolentStations, Solent);

        var warning = AxisOrderFindings.ToFinding(swapped, "S-104")!;
        Assert.Equal("S104-PROJ-AXIS", warning.RuleId);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Equal(Solent, warning.BoundingBox);

        Assert.Null(AxisOrderFindings.ToFinding(unverified, "S-104"));
        Assert.Null(AxisOrderFindings.ToFinding(verified, "S-104"));
    }

    [Fact]
    public void Append_counts_the_check_as_a_rule()
    {
        var verified = AxisOrderCheck.Assess(SolentStations, Solent);
        var swapped = AxisOrderCheck.Assess(Swapped(SolentStations), Solent);

        Assert.Null(AxisOrderFindings.Append(null, null, "S-104"));

        var clean = AxisOrderFindings.Append(ValidationReport.Empty, verified, "S-104")!;
        Assert.Equal(1, clean.RulesEvaluated);
        Assert.Empty(clean.Findings);

        var flagged = AxisOrderFindings.Append(null, swapped, "S-104")!;
        Assert.Equal(1, flagged.RulesWithFindings);
        Assert.Single(flagged.Findings);
    }
}
