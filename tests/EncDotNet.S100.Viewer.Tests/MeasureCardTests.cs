using System.Globalization;
using System.Text.Json;
using Avalonia.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The measure card, its shared session, its keys and the
/// <c>set_measure</c> / <c>get_measure</c> MCP tools (#1130, slice 2).
/// </summary>
public sealed class MeasureCardTests : IDisposable
{
    // Accuracy1: Viking to Corund Cape.
    private const string Viking = "32°35.224'S 061°17.710'E";
    private const string CorundCape = "32°27.436'S  060°58.609'E";
    private const string WormHead = "32°31.958'S 060°54.337'E";

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public MeasureCardTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static (MeasureSession Session, MeasureCardViewModel Card) Create(ViewerSettings? settings = null)
    {
        var session = new MeasureSession(settings);
        return (session, new MeasureCardViewModel(session));
    }

    private static void Type(MeasureCardViewModel card, string from, string to)
    {
        card.FromText = from;
        Assert.True(card.CommitFrom());
        card.ToText = to;
        Assert.True(card.CommitTo());
    }

    [Fact]
    public void Starts_with_the_hint_and_rhumb()
    {
        var (_, card) = Create();

        Assert.True(card.ShowsHint);
        Assert.False(card.HasLeg);
        Assert.True(card.IsRhumb);
        Assert.False(card.IsToEnabled);
        Assert.False(card.CanCopy);
        Assert.Equal("Sphere · R 6,371 km", MeasureCardViewModel.ModelText);
    }

    [Fact]
    public void Typed_accuracy1_great_circle_shows_both_units_and_both_bearings()
    {
        var (session, card) = Create();
        session.SetGeometry(LegGeometry.GreatCircle);

        Type(card, Viking, CorundCape);

        Assert.False(card.ShowsHint);
        Assert.Equal("17.90 NM", card.DistanceText);
        Assert.Equal("33.154 km", card.DistanceOtherText);
        Assert.Equal("295.7°T", card.BearingText);
        Assert.Equal("115.9°T", card.ReverseText);
        Assert.Equal("Distance 17.90 nautical miles", card.DistanceName);
        Assert.Equal("Reverse 115.9 degrees true", card.ReverseName);
        Assert.Equal("32°35.224'S  061°17.710'E", card.FromText);
        Assert.Equal(MeasurePathState.MeasurePhase.Finalised, session.State.Phase);
        Assert.False(card.HasMultipleLegs);
    }

    [Fact]
    public void Rhumb_reverse_is_forward_plus_180()
    {
        var (_, card) = Create();
        Type(card, Viking, CorundCape);

        Assert.Equal("295.8°T", card.BearingText);
        Assert.Equal("115.8°T", card.ReverseText);
    }

    [Fact]
    public void Metric_unit_puts_nautical_miles_on_the_second_line()
    {
        var (session, card) = Create();
        Type(card, Viking, CorundCape);

        session.Unit = DistanceUnit.Metric;

        Assert.Equal("33.154 km", card.DistanceText);
        Assert.Equal("17.90 NM", card.DistanceOtherText);
        Assert.StartsWith("Leg 1: 33.154 km · 295.8°T", session.State.FormatSummary(session.Unit));
    }

    [Fact]
    public void A_position_that_does_not_parse_is_refused_and_keeps_the_point()
    {
        var (session, card) = Create();
        Type(card, Viking, CorundCape);

        card.ToText = "not a place";
        Assert.False(card.CommitTo());

        Assert.True(card.HasToError);
        Assert.Equal("17.90 NM", card.DistanceText);
        Assert.Equal(2, session.State.Waypoints.Count);

        // Esc reverts; a second Esc has nothing to revert, so it can leave the tool.
        Assert.True(card.RevertTo());
        Assert.False(card.HasToError);
        Assert.Equal("32°27.436'S  060°58.609'E", card.ToText);
        Assert.False(card.RevertTo());
    }

    [Fact]
    public void Editing_to_moves_the_shown_legs_end()
    {
        var (session, card) = Create();
        Type(card, Viking, CorundCape);

        card.ToText = "-32.5, 61.0";
        Assert.True(card.CommitTo());

        Assert.Equal(2, session.State.Waypoints.Count);
        Assert.Equal(new GeoPosition(-32.5, 61.0), session.State.Waypoints[1]);
    }

    [Fact]
    public void A_field_being_typed_in_is_not_overwritten_by_path_changes()
    {
        var (session, card) = Create();
        Type(card, Viking, CorundCape);

        card.FromText = "32°";
        session.SetGeometry(LegGeometry.GreatCircle);

        Assert.Equal("32°", card.FromText);
    }

    [Fact]
    public void Committed_positions_are_reported_for_panning()
    {
        var (session, card) = Create();
        var committed = new List<GeoPosition>();
        session.PositionCommitted += (_, p) => committed.Add(p);

        Type(card, Viking, CorundCape);

        Assert.Equal(2, committed.Count);
    }

    [Fact]
    public void Multi_leg_paths_step_through_legs_and_total_both_units()
    {
        var (session, card) = Create();
        Assert.True(LatLonFormatter.TryParse(Viking, out var aLat, out var aLon));
        Assert.True(LatLonFormatter.TryParse(CorundCape, out var bLat, out var bLon));
        Assert.True(LatLonFormatter.TryParse(WormHead, out var cLat, out var cLon));
        session.State.SetWaypoints([new(aLat, aLon), new(bLat, bLon), new(cLat, cLon)]);
        session.NotifyChanged();

        Assert.True(card.HasMultipleLegs);
        Assert.Equal("Leg 2 of 2", card.LegCaption);
        Assert.False(card.CanGoNext);
        Assert.True(card.CanGoPrevious);
        Assert.Contains(" · ", card.TotalText);
        Assert.EndsWith(" km", card.TotalText);

        card.PreviousLegCommand.Execute(null);

        Assert.Equal("Leg 1 of 2", card.LegCaption);
        Assert.Equal("17.90 NM", card.DistanceText);
        Assert.Equal("32°35.224'S  061°17.710'E", card.FromText);

        // From and To are the selected leg's ends.
        card.ToText = "-32.5, 61.0";
        Assert.True(card.CommitTo());
        Assert.Equal(new GeoPosition(-32.5, 61.0), session.State.Waypoints[1]);
        Assert.Equal(3, session.State.Waypoints.Count);
    }

    [Fact]
    public void Copy_includes_positions_geometry_both_units_bearings_and_the_model()
    {
        var (session, card) = Create();
        session.SetGeometry(LegGeometry.GreatCircle);
        Type(card, Viking, CorundCape);
        string? copied = null;
        card.CopyRequested += (_, text) => copied = text;

        card.CopyCommand.Execute(null);

        Assert.NotNull(copied);
        Assert.Contains("Great circle", copied);
        Assert.Contains("32°35.224'S  061°17.710'E", copied);
        Assert.Contains("-32.587067, 61.295167", copied);
        Assert.Contains("17.90 NM  (33.154 km)", copied);
        Assert.Contains("295.7°T", copied);
        Assert.Contains("115.9°T", copied);
        Assert.Contains("Sphere · R 6,371 km", copied);
    }

    [Fact]
    public void Geometry_is_remembered_in_settings()
    {
        var settings = new ViewerSettings { IsReadOnly = true };
        var (session, card) = Create(settings);

        card.GreatCircleCommand.Execute(null);

        Assert.Equal(nameof(LegGeometry.GreatCircle), settings.MeasureGeometry);
        Assert.Equal(LegGeometry.GreatCircle, new MeasureSession(settings).Geometry);
        Assert.True(card.IsGreatCircle);

        session.ToggleGeometry();
        Assert.Equal(nameof(LegGeometry.Rhumb), settings.MeasureGeometry);
    }

    [Fact]
    public void Status_and_map_labels_show_the_primary_unit_only()
    {
        var (session, card) = Create();
        Type(card, Viking, CorundCape);
        var leg = session.State.CurrentLeg!.Value;

        Assert.Equal("Leg 1: 17.90 NM · 295.8°T  |  Total: 17.90 NM", session.State.FormatSummary(session.Unit));
        Assert.Equal("17.90 NM · 295.8°", MeasureOverlayLayer.LegLabelText(leg, session.Unit));
    }

    [Fact]
    public void Great_circle_legs_draw_densified_and_label_at_their_middle()
    {
        var leg = MeasureLeg.Between(1, false, LegGeometry.GreatCircle, new GeoPosition(40, -10), new GeoPosition(40, -70));

        var path = MeasureOverlayLayer.LegPath(leg);
        var anchor = MeasureOverlayLayer.LabelAnchor(leg);

        Assert.True(path.Count > 10);
        Assert.Equal(leg.From, path[0]);
        Assert.Equal(leg.To, path[^1]);
        // The great circle bows north of both ends.
        Assert.True(anchor.Latitude > 44);
    }

    [Fact]
    public void Rhumb_label_takes_the_short_way_across_the_antimeridian()
    {
        var leg = MeasureLeg.Between(1, false, LegGeometry.Rhumb, new GeoPosition(0, 179), new GeoPosition(0, -179));

        var anchor = MeasureOverlayLayer.LabelAnchor(leg);

        Assert.Equal(180, Math.Abs(anchor.Longitude), 6);
    }

    [Theory]
    [InlineData(nameof(Key.G), true, false)]
    [InlineData(nameof(Key.G), false, true)]
    public void G_switches_geometry_only_on_the_map_while_measuring(string keyName, bool inTextField, bool switches)
    {
        var (session, card) = Create();
        session.IsActive = true;
        var scope = inTextField ? RouteEditorKeys.KeyScope.TextInput : RouteEditorKeys.KeyScope.Map;

        var handled = MeasureKeys.Handle(session, card, Enum.Parse<Key>(keyName), KeyModifiers.None, scope);

        Assert.Equal(switches, handled);
        Assert.Equal(switches, session.Geometry == LegGeometry.GreatCircle);
    }

    [Fact]
    public void Keys_do_nothing_while_measure_is_off()
    {
        var (session, card) = Create();

        Assert.False(MeasureKeys.Handle(session, card, Key.G, KeyModifiers.None, RouteEditorKeys.KeyScope.Map));
        Assert.Equal(LegGeometry.Rhumb, session.Geometry);
    }

    [Fact]
    public void Delete_clears_and_command_c_copies()
    {
        var (session, card) = Create();
        session.IsActive = true;
        Type(card, Viking, CorundCape);
        string? copied = null;
        card.CopyRequested += (_, text) => copied = text;

        Assert.True(MeasureKeys.Handle(session, card, Key.C, KeyModifiers.Meta, RouteEditorKeys.KeyScope.Map));
        Assert.NotNull(copied);

        Assert.True(MeasureKeys.Handle(session, card, Key.Delete, KeyModifiers.None, RouteEditorKeys.KeyScope.Map));
        Assert.Empty(session.State.Waypoints);
        Assert.True(card.ShowsHint);
    }

    [Fact]
    public void Clicking_resets_the_selected_leg_to_follow_the_last()
    {
        var state = new MeasurePathState();
        state.Click(0, 0);
        state.Click(0, 1);
        state.Click(0, 2);

        Assert.True(state.SelectLeg(1));
        Assert.Equal(1, state.CurrentLeg!.Value.Index);

        state.Click(0, 3);
        Assert.Null(state.SelectedLeg);
        Assert.Equal(3, state.CurrentLeg!.Value.Index);
        Assert.False(state.SelectLeg(9));
    }

    // --- MCP ------------------------------------------------------------

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static MeasureDto Value(ToolResult<MeasureDto> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e!.ToString() : null);
        return value!;
    }

    [Fact]
    public async Task Set_measure_turns_the_tool_on_and_returns_unrounded_values()
    {
        var (session, card) = Create();
        var activations = 0;
        session.ActivationRequested += (_, _) => { activations++; session.IsActive = true; };
        var set = new SetMeasureTool(session, card, new InlineInvoker());

        var dto = Value(await set.InvokeAsync(
            Json("""["32°35.224'S 061°17.710'E", {"lat": -32.457266666666667, "lon": 60.976816666666667}]"""),
            "greatCircle", null, TestContext.Current.CancellationToken));

        Assert.Equal(1, activations);
        Assert.True(dto.Active);
        Assert.Equal("greatCircle", dto.Geometry);
        Assert.Equal("finalised", dto.Phase);
        Assert.Equal("sphere", dto.EarthModel.Name);
        Assert.Equal(GeodesicHelper.EarthRadiusMetres, dto.EarthModel.RadiusMetres);
        var leg = Assert.Single(dto.Legs);
        Assert.Equal(MarineGeodesy(leg.From, leg.To), leg.DistanceMetres);
        Assert.InRange(leg.DistanceMetres, 33154.0, 33155.0);
        Assert.NotEqual(Math.Round(leg.DistanceMetres), leg.DistanceMetres);
        Assert.Equal(leg.DistanceMetres / PassageGeodesy.MetersPerNauticalMile, leg.DistanceNm, 9);
        Assert.InRange(leg.BearingDeg, 295.65, 295.75);
        Assert.InRange(leg.ReverseBearingDeg, 115.85, 115.95);
        Assert.Equal(1, dto.SelectedLeg);
        Assert.Equal("17.90 NM", dto.Card!.Distance);
        Assert.Equal("33.154 km", dto.Card.DistanceOther);
        Assert.Null(dto.Card.Total);

        static double MarineGeodesy(MeasurePointDto a, MeasurePointDto b) =>
            Viewer.Geodesy.MarineGeodesy.DistanceMetres(LegGeometry.GreatCircle, new(a.Lat, a.Lon), new(b.Lat, b.Lon));
    }

    [Fact]
    public async Task Set_measure_selects_a_leg_and_get_measure_reads_it()
    {
        var (session, card) = Create();
        var set = new SetMeasureTool(session, card, new InlineInvoker());
        var get = new GetMeasureTool(session, card, new InlineInvoker());

        Value(await set.InvokeAsync(Json("""["0, 0", "0, 1", "0, 2"]"""), "rhumb", 1, TestContext.Current.CancellationToken));
        var dto = Value(await get.InvokeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, dto.SelectedLeg);
        Assert.Equal(2, dto.Legs.Count);
        Assert.Equal(dto.Legs.Sum(l => l.DistanceMetres), dto.TotalMetres);
        Assert.NotNull(dto.Card!.Total);
        Assert.Equal("Leg 1 of 2", card.LegCaption);
    }

    [Theory]
    [InlineData("""["nowhere"]""", null, null, "points[0]")]
    [InlineData("""[{"lat": 91, "lon": 0}]""", null, null, "points[0]")]
    [InlineData("""{"lat": 1}""", null, null, "points")]
    [InlineData("""[]""", "loxo-ish", null, "geometry")]
    [InlineData("""["0, 0", "0, 1"]""", null, 3, "selectedLeg")]
    public async Task Set_measure_refuses_bad_arguments(string points, string? geometry, int? selectedLeg, string argument)
    {
        var (session, card) = Create();
        var set = new SetMeasureTool(session, card, new InlineInvoker());

        var result = await set.InvokeAsync(Json(points), geometry, selectedLeg, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(argument, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task Empty_points_clear_the_path()
    {
        var (session, card) = Create();
        var set = new SetMeasureTool(session, card, new InlineInvoker());
        Value(await set.InvokeAsync(Json("""["0, 0", "0, 1"]"""), null, null, TestContext.Current.CancellationToken));

        var dto = Value(await set.InvokeAsync(Json("[]"), null, null, TestContext.Current.CancellationToken));

        Assert.Empty(dto.Points);
        Assert.Empty(dto.Legs);
        Assert.Equal("idle", dto.Phase);
        Assert.Null(dto.Card);
        Assert.True(card.ShowsHint);
    }
}
