using System.Text.Json;
using Avalonia.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using Field = EncDotNet.S100.Viewer.ViewModels.RangeBearingViewModel.Field;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Range &amp; bearing tool (#1130 D11–D16, D23): its model, card, MCP
/// tools and keys, and the S-164 Accuracy4–6 and Accuracy12 checks run
/// through it.
/// </summary>
public class RangeBearingToolTests
{
    private static readonly GeoPosition Viking = Dm(32, 35.224, 'S', 61, 17.710, 'E');
    private static readonly GeoPosition CorundCape = Dm(32, 27.436, 'S', 60, 58.609, 'E');
    private static readonly GeoPosition Castlerigg = Dm(32, 23.280, 'S', 60, 58.496, 'E');
    private static readonly GeoPosition CorundCapeAccuracy3 = Dm(32, 27.447, 'S', 60, 58.599, 'E');
    private static readonly GeoPosition WormHead = Dm(32, 31.958, 'S', 60, 54.337, 'E');

    public static TheoryData<string, string, double, double, string> DirectProblems => new()
    {
        { "Accuracy4", nameof(Viking), 33193.554, 295.614, nameof(CorundCape) },
        { "Accuracy5", nameof(Viking), 37326.351, 306.172, nameof(Castlerigg) },
        { "Accuracy6", nameof(CorundCapeAccuracy3), 10680.859, 218.665, nameof(WormHead) },
    };

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    [Theory]
    [MemberData(nameof(DirectProblems))]
    public void Origin_range_and_bearing_give_the_expected_end(string test, string origin, double metres, double bearing, string expected)
    {
        // Accuracy4–6: type Origin, Range and Bearing, read End.
        var state = new RangeBearingState { Geometry = LegGeometry.GreatCircle };
        state.SetOrigin(Position(origin));
        state.SetRange(metres);
        state.SetBearing(bearing);

        Assert.Equal(RangeBearingState.RangeBearingPhase.Fixed, state.Phase);
        var miss = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, state.End!.Value, Position(expected));
        Assert.True(miss <= Math.Max(30.0, metres * 0.01), $"{test}: end is {miss:F1} m from the listed position");
    }

    [Theory]
    [MemberData(nameof(DirectProblems))]
    public void The_card_gives_the_expected_end_from_typed_text(string test, string origin, double metres, double bearing, string expected)
    {
        var state = new RangeBearingState { Geometry = LegGeometry.GreatCircle };
        var card = Card(state, DistanceUnit.Metric);
        var start = Position(origin);

        Type(card, Field.Origin, LatLonFormatter.Format(start.Latitude, start.Longitude));
        Type(card, Field.Range, FormattableString.Invariant($"{metres} m"));
        Type(card, Field.Bearing, FormattableString.Invariant($"{bearing}°T"));

        Assert.True(state.IsRangeLocked && state.IsBearingLocked);
        var miss = MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, state.End!.Value, Position(expected));
        Assert.True(miss <= Math.Max(30.0, metres * 0.01), $"{test}: end is {miss:F1} m from the listed position");
        Assert.Equal(LatLonFormatter.Format(state.End.Value.Latitude, state.End.Value.Longitude), card.EndText);
    }

    [Fact]
    public void A_vrm_of_2000000_m_lies_on_the_first_2000000_m_ring()
    {
        // Accuracy12: range 2,000,000 m with rings every 2,000,000 m.
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        state.SetRange(2_000_000);
        state.SetRings(true, 2_000_000, 3);

        Assert.Equal([2_000_000.0, 4_000_000.0, 6_000_000.0], state.RingRanges);

        var vrm = MarineGeodesy.GeodesicCircle(Viking, state.RangeMetres!.Value);
        var ring = MarineGeodesy.GeodesicCircle(Viking, state.RingRanges[0]);
        Assert.Equal(vrm, ring);
        foreach (var point in vrm)
        {
            Assert.Equal(2_000_000.0, MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, Viking, point), 0.01);
        }

        // On the map the VRM and the first ring are drawn through the same points.
        var lines = Lines(RangeBearingOverlayLayer.Build(state, MeasureOverlayAppearance.Default, DistanceUnit.Metric));
        var ringLine = lines[0];
        Assert.Contains(lines.Skip(1), l => l.Coordinates.SequenceEqual(ringLine.Coordinates));
    }

    [Fact]
    public void A_circle_round_a_pole_is_drawn_open_and_never_filled()
    {
        var state = new RangeBearingState();
        state.SetOrigin(new GeoPosition(85, 30));
        state.SetRange(1_000_000);

        var features = RangeBearingOverlayLayer.Build(state, MeasureOverlayAppearance.Default, DistanceUnit.NauticalMiles);

        Assert.DoesNotContain(features, f => f is GeometryFeature { Geometry: Polygon });
        var circle = Lines(features);
        Assert.NotEmpty(circle);
        // Round the pole the circle crosses the antimeridian once: it's cut there into an open line.
        Assert.All(circle, l => Assert.False(l.IsClosed));
        Assert.All(circle.SelectMany(l => l.Coordinates), c => Assert.True(double.IsFinite(c.Y)));
    }

    [Fact]
    public void Clicks_place_the_origin_follow_the_cursor_fix_the_end_and_start_again()
    {
        var state = new RangeBearingState();
        Assert.False(state.Hover(CorundCape));

        state.Click(Viking);
        Assert.Equal(RangeBearingState.RangeBearingPhase.Following, state.Phase);
        Assert.True(state.Hover(CorundCape));
        Assert.Equal(MarineGeodesy.DistanceMetres(LegGeometry.Rhumb, Viking, CorundCape), state.RangeMetres!.Value, 1e-6);
        Assert.Equal(MarineGeodesy.InitialBearingDegrees(LegGeometry.Rhumb, Viking, CorundCape), state.BearingDegrees!.Value, 1e-9);
        Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, state.End!.Value, CorundCape) < 0.01);

        state.Click(CorundCape);
        Assert.Equal(RangeBearingState.RangeBearingPhase.Fixed, state.Phase);
        Assert.False(state.Hover(Castlerigg));

        state.Click(Castlerigg);
        Assert.Equal(Castlerigg, state.Origin);
        Assert.Null(state.RangeMetres);
        Assert.Equal(RangeBearingState.RangeBearingPhase.Following, state.Phase);

        Assert.True(state.Backstep());
        Assert.Null(state.Origin);
        Assert.Equal(RangeBearingState.RangeBearingPhase.PlacingOrigin, state.Phase);
    }

    [Fact]
    public void A_locked_range_lets_the_cursor_swing_only_the_bearing()
    {
        var state = new RangeBearingState();
        state.SetRange(5000);
        state.Click(Viking);
        state.Hover(CorundCape);

        Assert.Equal(5000, state.RangeMetres);
        Assert.Equal(MarineGeodesy.InitialBearingDegrees(LegGeometry.Rhumb, Viking, CorundCape), state.BearingDegrees!.Value, 1e-9);
        Assert.Equal(5000, MarineGeodesy.DistanceMetres(LegGeometry.Rhumb, Viking, state.End!.Value), 0.01);
    }

    [Fact]
    public void A_locked_bearing_lets_the_cursor_change_only_the_range()
    {
        var state = new RangeBearingState();
        state.SetBearing(45);
        state.Click(Viking);
        state.Hover(CorundCape);

        Assert.Equal(45, state.BearingDegrees);
        Assert.Equal(MarineGeodesy.DistanceMetres(LegGeometry.Rhumb, Viking, CorundCape), state.RangeMetres!.Value, 1e-6);
    }

    [Fact]
    public void Unlocking_a_fixed_end_lets_it_follow_the_cursor_again()
    {
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        state.SetRange(5000);
        state.SetBearing(90);
        Assert.Equal(RangeBearingState.RangeBearingPhase.Fixed, state.Phase);

        state.UnlockBearing();
        Assert.Equal(RangeBearingState.RangeBearingPhase.Following, state.Phase);
        Assert.Equal(90, state.BearingDegrees);
        state.Hover(CorundCape);
        Assert.Equal(5000, state.RangeMetres);
        Assert.NotEqual(90, state.BearingDegrees);
    }

    [Fact]
    public void Delete_clears_the_origin_and_the_locks()
    {
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        state.SetRange(5000);

        Assert.True(state.Discard());
        Assert.Null(state.Origin);
        Assert.False(state.IsRangeLocked);
        Assert.False(state.Discard());
    }

    [Fact]
    public void The_geometry_is_remembered_and_starts_at_rhumb()
    {
        var settings = new ViewerSettings { IsReadOnly = true };
        Assert.Equal(LegGeometry.Rhumb, new RangeBearingState(settings).Geometry);

        var state = new RangeBearingState(settings) { Geometry = LegGeometry.GreatCircle };

        Assert.Equal(nameof(LegGeometry.GreatCircle), settings.RangeBearingGeometry);
        Assert.Equal(LegGeometry.GreatCircle, new RangeBearingState(settings).Geometry);
    }

    [Fact]
    public void Switching_the_geometry_moves_the_end_along_the_other_line()
    {
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        state.SetRange(3_000_000);
        state.SetBearing(60);
        var rhumbEnd = state.End!.Value;

        state.Geometry = LegGeometry.GreatCircle;

        Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, rhumbEnd, state.End!.Value) > 10_000);
        Assert.NotEqual(state.BearingDegrees!.Value + 180, state.ReverseBearingDegrees!.Value, 1);
    }

    [Fact]
    public void The_card_refuses_a_bare_M_and_keeps_the_field()
    {
        var state = new RangeBearingState();
        var card = Card(state, DistanceUnit.NauticalMiles);

        Type(card, Field.Range, "12 M");

        Assert.NotNull(card.RangeError);
        Assert.Equal("12 M", card.RangeText);
        Assert.Null(state.RangeMetres);
        Assert.True(card.HasPendingEdit(Field.Range));

        card.Revert(Field.Range);
        Assert.Null(card.RangeError);
        Assert.Equal(string.Empty, card.RangeText);
    }

    [Fact]
    public void The_card_shows_the_range_in_the_primary_unit_with_the_other_underneath()
    {
        var state = new RangeBearingState();
        var card = Card(state, DistanceUnit.NauticalMiles);

        Type(card, Field.Range, "2000000 m");

        Assert.Equal(2_000_000, state.RangeMetres);
        Assert.Equal(MeasureFormat.Distance(2_000_000, DistanceUnit.NauticalMiles), card.RangeText);
        Assert.Equal(MeasureFormat.OtherDistance(2_000_000, DistanceUnit.NauticalMiles), card.RangeOther);
        Assert.True(card.IsRangeLocked);

        // Clearing the field unlocks it.
        Type(card, Field.Range, "");
        Assert.False(state.IsRangeLocked);
    }

    [Fact]
    public void Tabbing_through_a_field_without_typing_locks_nothing()
    {
        var state = new RangeBearingState();
        state.Click(Viking);
        state.Hover(CorundCape);
        var card = Card(state, DistanceUnit.NauticalMiles);

        card.BeginEdit(Field.Bearing);
        card.Commit(Field.Bearing);

        Assert.False(state.IsBearingLocked);
    }

    [Fact]
    public void A_field_being_typed_in_is_not_overwritten_by_the_cursor()
    {
        var state = new RangeBearingState();
        state.Click(Viking);
        var card = Card(state, DistanceUnit.NauticalMiles);

        card.BeginEdit(Field.Bearing);
        card.BearingText = "12";
        state.Hover(CorundCape);

        Assert.Equal("12", card.BearingText);
        Assert.NotEqual(string.Empty, card.RangeText);
    }

    [Fact]
    public void A_bad_origin_shows_the_hint_and_keeps_the_old_origin()
    {
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        var card = Card(state, DistanceUnit.NauticalMiles);

        Type(card, Field.Origin, "nowhere");

        Assert.NotNull(card.OriginError);
        Assert.Equal(Viking, state.Origin);
    }

    [Fact]
    public void Nudging_turns_the_bearing_and_steps_the_range_by_its_last_digit()
    {
        var state = new RangeBearingState();
        state.SetBearing(359.5);
        state.SetRange(1852);
        var card = Card(state, DistanceUnit.NauticalMiles);

        card.NudgeBearing(1);
        card.NudgeRange(1);

        Assert.Equal(0.5, state.BearingDegrees!.Value, 1e-9);
        Assert.Equal(1852 * 1.01, state.RangeMetres!.Value, 1e-6);
    }

    [Fact]
    public void Rings_are_typed_on_the_card()
    {
        var state = new RangeBearingState();
        var card = Card(state, DistanceUnit.Metric);

        Type(card, Field.RingInterval, "2,000 km");
        Type(card, Field.RingCount, "2");

        Assert.Equal([2_000_000.0, 4_000_000.0], state.RingRanges);
        Type(card, Field.RingCount, "0");
        Assert.NotNull(card.RingError);
    }

    [Fact]
    public async Task Set_range_bearing_turns_the_tool_on_locks_and_returns_unrounded_values()
    {
        var state = new RangeBearingState();
        var settings = new ViewerSettings { IsReadOnly = true };
        var set = new SetRangeBearingTool(state, settings, new InlineInvoker());
        var get = new GetRangeBearingTool(state, new InlineInvoker());

        var dto = Value(await set.InvokeAsync(
            Json("\"32°35.224'S 061°17.710'E\""), Json("33193.554"), 295.614, "greatCircle",
            Json("{\"interval\": \"2,000 km\", \"count\": 2}"), TestContext.Current.CancellationToken));

        Assert.True(state.IsToolActive);
        Assert.True(dto.Active);
        Assert.Equal(("fixed", "greatCircle", true, true), (dto.Phase, dto.Geometry, dto.RangeLocked, dto.BearingLocked));
        Assert.Equal(33193.554, dto.RangeMetres);
        Assert.Equal(295.614, dto.BearingDegrees);
        var end = state.End!.Value;
        Assert.Equal((end.Latitude, end.Longitude), (dto.End!.Lat, dto.End.Lon));
        Assert.Equal(state.ReverseBearingDegrees, dto.ReverseBearingDegrees);
        Assert.True(MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, end, CorundCape) <= 0.01 * 33193.554);
        Assert.Equal([2_000_000.0, 4_000_000.0], dto.Rings.RangesMetres);
        Assert.Equal(dto.End, Value(await get.InvokeAsync(TestContext.Current.CancellationToken)).End);

        // {lat, lon} origins and a bare M.
        var moved = Value(await set.InvokeAsync(Json("{\"lat\": 10, \"lon\": 20}"), null, null, null, Json("false"), TestContext.Current.CancellationToken));
        Assert.Equal((10.0, 20.0), (moved.Origin!.Lat, moved.Origin.Lon));
        Assert.False(moved.Rings.Enabled);
        Assert.False((await set.InvokeAsync(null, Json("\"5 M\""), null, null, null, TestContext.Current.CancellationToken)).TryGetValue(out _));
    }

    [Theory]
    [InlineData("origin", "\"nowhere\"", null, null)]
    [InlineData("range", null, "-5", null)]
    [InlineData("geometry", null, null, "orthodrome")]
    public async Task Set_range_bearing_rejects_bad_arguments_before_changing_anything(string parameter, string? origin, string? range, string? geometry)
    {
        var state = new RangeBearingState();
        var set = new SetRangeBearingTool(state, new ViewerSettings { IsReadOnly = true }, new InlineInvoker());

        var result = await set.InvokeAsync(
            origin is null ? null : Json(origin), range is null ? null : Json(range), null, geometry, null,
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
        Assert.False(state.IsToolActive);
    }

    [Fact]
    public void G_switches_the_geometry_and_Delete_clears()
    {
        var state = new RangeBearingState();
        var copied = 0;

        Assert.True(RangeBearingKeys.Handle(state, Key.G, KeyModifiers.None, () => copied++));
        Assert.Equal(LegGeometry.GreatCircle, state.Geometry);
        Assert.False(RangeBearingKeys.Handle(state, Key.C, KeyModifiers.Meta, () => copied++));

        state.SetOrigin(Viking);
        Assert.True(RangeBearingKeys.Handle(state, Key.C, KeyModifiers.Control, () => copied++));
        Assert.Equal(1, copied);
        Assert.True(RangeBearingKeys.Handle(state, Key.Delete, KeyModifiers.None, () => copied++));
        Assert.Null(state.Origin);
        Assert.False(RangeBearingKeys.Handle(state, Key.G, KeyModifiers.Shift, () => copied++));
    }

    [Fact]
    public void Copy_text_has_both_unit_systems_and_the_earth_model()
    {
        var state = new RangeBearingState();
        state.SetOrigin(Viking);
        state.SetRange(33193.554);
        state.SetBearing(295.614);

        var text = RangeBearingText.Copy(state, DistanceUnit.NauticalMiles)!;

        Assert.Contains(MeasureFormat.Distance(33193.554, DistanceUnit.NauticalMiles), text);
        Assert.Contains(MeasureFormat.OtherDistance(33193.554, DistanceUnit.NauticalMiles), text);
        Assert.Contains("WGS-84 ellipsoid", text);
        Assert.Contains("End: ", text);
    }

    private static RangeBearingViewModel Card(RangeBearingState state, DistanceUnit unit) =>
        new(state, () => unit, () => { });

    private static void Type(RangeBearingViewModel card, Field field, string text)
    {
        card.BeginEdit(field);
        switch (field)
        {
            case Field.Origin: card.OriginText = text; break;
            case Field.Range: card.RangeText = text; break;
            case Field.Bearing: card.BearingText = text; break;
            case Field.RingInterval: card.RingIntervalText = text; break;
            default: card.RingCountText = text; break;
        }

        card.Commit(field);
    }

    private static List<LineString> Lines(IEnumerable<Mapsui.IFeature> features) =>
        features.OfType<GeometryFeature>().Select(f => f.Geometry).OfType<LineString>().ToList();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static T Value<T>(ToolResult<T> result) where T : class =>
        result.TryGetValue(out var value) ? value! : throw new Xunit.Sdk.XunitException(result.TryGetError(out var e) ? e.Message : "error");

    private static GeoPosition Position(string name) => name switch
    {
        nameof(Viking) => Viking,
        nameof(CorundCape) => CorundCape,
        nameof(Castlerigg) => Castlerigg,
        nameof(CorundCapeAccuracy3) => CorundCapeAccuracy3,
        nameof(WormHead) => WormHead,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static GeoPosition Dm(int latDeg, double latMin, char ns, int lonDeg, double lonMin, char ew) => new(
        (latDeg + latMin / 60.0) * (ns == 'S' ? -1 : 1),
        (lonDeg + lonMin / 60.0) * (ew == 'W' ? -1 : 1));
}
