using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The measure tool's state, for <c>get_measure</c> and <c>set_measure</c> (#1130, D23).</summary>
[Description("The measure tool: whether it's on, its geometry and earth model, the path's points and legs (unrounded), the total, and what the card shows.")]
internal sealed record MeasureDto(
    [property: Description("True while the measure tool is on.")] bool Active,
    [property: Description("rhumb or greatCircle: what every leg is measured along. Remembered per user.")] string Geometry,
    [property: Description("The earth the distances and bearings are computed on.")] MeasureEarthModelDto EarthModel,
    [property: Description("idle (no path), drawing (clicking adds points; a pending leg follows the cursor) or finalised.")] string Phase,
    [property: Description("The path's points in order.")] IReadOnlyList<MeasurePointDto> Points,
    [property: Description("The legs: leg 1 joins points 1 and 2. A pending leg to the cursor is last while drawing.")] IReadOnlyList<MeasureLegDto> Legs,
    [property: Description("One-based leg the card shows, or null with no leg.")] int? SelectedLeg,
    [property: Description("Length of the whole path in metres, unrounded.")] double TotalMetres,
    [property: Description("Length of the whole path in nautical miles, unrounded.")] double TotalNm,
    [property: Description("What the card shows for the selected leg, as displayed (rounded), or null with no leg.")] MeasureCardDto? Card);

/// <summary>The earth model (D17).</summary>
[Description("The earth model distances and bearings use.")]
internal sealed record MeasureEarthModelDto(
    [property: Description("wgs84: geodesics and rhumb lines on the WGS-84 ellipsoid.")] string Name,
    [property: Description("As the card's footer shows it: \"WGS-84 ellipsoid\".")] string Text);

/// <summary>A position.</summary>
[Description("A position.")]
internal sealed record MeasurePointDto(
    [property: Description("Latitude in decimal degrees.")] double Lat,
    [property: Description("Longitude in decimal degrees.")] double Lon,
    [property: Description("In the app's degrees and decimal minutes, as the card shows it.")] string Text);

/// <summary>One leg, unrounded.</summary>
[Description("One leg of the measured path, unrounded.")]
internal sealed record MeasureLegDto(
    [property: Description("One-based leg number.")] int Index,
    [property: Description("True for the pending leg that follows the cursor while drawing.")] bool Pending,
    [property: Description("The leg's start.")] MeasurePointDto From,
    [property: Description("The leg's end.")] MeasurePointDto To,
    [property: Description("Length along the geometry in metres.")] double DistanceMetres,
    [property: Description("Length along the geometry in nautical miles.")] double DistanceNm,
    [property: Description("True bearing in degrees at From towards To: a rhumb line's constant bearing or a great circle's initial bearing.")] double BearingDeg,
    [property: Description("True bearing in degrees at To back towards From. On a great circle it isn't BearingDeg ± 180.")] double ReverseBearingDeg);

/// <summary>What the card shows.</summary>
[Description("The measure card's values as displayed.")]
internal sealed record MeasureCardDto(
    [property: Description("The From field.")] string From,
    [property: Description("The To field.")] string To,
    [property: Description("Distance in the Distance unit setting, e.g. \"17.90 NM\".")] string Distance,
    [property: Description("Distance in the other unit system, e.g. \"33.154 km\".")] string DistanceOther,
    [property: Description("Forward bearing, e.g. \"295.7°T\".")] string Bearing,
    [property: Description("Reverse bearing, e.g. \"115.9°T\".")] string Reverse,
    [property: Description("Total in both unit systems when the path has more than one leg, else null.")] string? Total);

/// <summary>Reads the measure tool.</summary>
internal sealed class GetMeasureTool
{
    public const string Name = "get_measure";

    private readonly MeasureSession _session;
    private readonly MeasureCardViewModel _card;
    private readonly IRouteEditInvoker _invoker;

    public GetMeasureTool(MeasureSession session, MeasureCardViewModel card, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(invoker);
        _session = session;
        _card = card;
        _invoker = invoker;
    }

    public Task<ToolResult<MeasureDto>> InvokeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<MeasureDto>.Ok(Dto(_session, _card)));
    }

    internal static MeasureDto Dto(MeasureSession session, MeasureCardViewModel card)
    {
        var state = session.State;
        var legs = state.ComputeLegs();
        var current = state.CurrentLeg;
        var total = state.TotalDistanceMetres();
        return new MeasureDto(
            session.IsActive,
            GeometryName(state.Geometry),
            new MeasureEarthModelDto("wgs84", MeasureSession.EarthModelText),
            state.Phase switch
            {
                MeasurePathState.MeasurePhase.Drawing => "drawing",
                MeasurePathState.MeasurePhase.Finalised => "finalised",
                _ => "idle",
            },
            state.Waypoints.Select(Point).ToList(),
            legs.Select(l => new MeasureLegDto(
                l.Index, l.IsRubberBand, Point(l.From), Point(l.To),
                l.DistanceMetres, l.DistanceNm, l.BearingDeg, l.ReverseBearingDeg)).ToList(),
            current?.Index,
            total,
            total / PassageGeodesy.MetersPerNauticalMile,
            current is null
                ? null
                : new MeasureCardDto(card.FromText, card.ToText, card.DistanceText, card.DistanceOtherText,
                    card.BearingText, card.ReverseText, card.HasMultipleLegs ? card.TotalText : null));
    }

    internal static string GeometryName(LegGeometry geometry) =>
        geometry == LegGeometry.GreatCircle ? "greatCircle" : "rhumb";

    private static MeasurePointDto Point(GeoPosition p) =>
        new(p.Latitude, p.Longitude, LatLonFormatter.Format(p.Latitude, p.Longitude));
}

/// <summary>Turns the measure tool on and sets its path, geometry and leg.</summary>
internal sealed class SetMeasureTool
{
    public const string Name = "set_measure";

    private readonly MeasureSession _session;
    private readonly MeasureCardViewModel _card;
    private readonly IRouteEditInvoker _invoker;

    public SetMeasureTool(MeasureSession session, MeasureCardViewModel card, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(invoker);
        _session = session;
        _card = card;
        _invoker = invoker;
    }

    public Task<ToolResult<MeasureDto>> InvokeAsync(
        JsonElement? points, string? geometry, int? selectedLeg, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LegGeometry? parsedGeometry = null;
        if (geometry is not null)
        {
            if (!TryParseGeometry(geometry, out var g))
                return Task.FromResult(ToolResult<MeasureDto>.Err(new InvalidArgument("geometry", $"'{geometry}' is not rhumb or greatCircle")));
            parsedGeometry = g;
        }

        List<GeoPosition>? positions = null;
        if (points is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } array)
        {
            if (TryParsePoints(array, out var parsed) is { } error)
                return Task.FromResult(ToolResult<MeasureDto>.Err(error));
            positions = parsed;
        }

        return _invoker.InvokeAsync(() =>
        {
            _session.RequestActivation();
            if (parsedGeometry is { } g)
                _session.SetGeometry(g);
            if (positions is not null)
                _session.State.SetWaypoints(positions);
            if (selectedLeg is { } leg && !_session.State.SelectLeg(leg) && _session.State.CurrentLeg?.Index != leg)
            {
                _session.NotifyChanged();
                return ToolResult<MeasureDto>.Err(new InvalidArgument(
                    "selectedLeg", $"{leg} is not a leg; the path has {_session.State.ComputeLegs().Count}"));
            }

            _session.NotifyChanged();
            return ToolResult<MeasureDto>.Ok(GetMeasureTool.Dto(_session, _card));
        });
    }

    internal static bool TryParseGeometry(string text, out LegGeometry geometry)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "rhumb" or "rhumbline" or "rhumb_line" or "loxodrome":
                geometry = LegGeometry.Rhumb;
                return true;
            case "greatcircle" or "great_circle" or "orthodrome" or "geodesic":
                geometry = LegGeometry.GreatCircle;
                return true;
            default:
                geometry = default;
                return false;
        }
    }

    /// <summary>
    /// Reads <c>points</c>: an array of position strings the card accepts or
    /// <c>{lat, lon}</c> objects.
    /// </summary>
    internal static ToolError? TryParsePoints(JsonElement points, out List<GeoPosition> positions)
    {
        positions = [];
        if (points.ValueKind != JsonValueKind.Array)
            return new InvalidArgument("points", "points must be an array of positions");

        var i = 0;
        foreach (var item in points.EnumerateArray())
        {
            var name = $"points[{i++}]";
            if (item.ValueKind == JsonValueKind.String)
            {
                if (!LatLonFormatter.TryParse(item.GetString(), out var lat, out var lon))
                    return new InvalidArgument(name, $"'{item.GetString()}' is not a position");
                positions.Add(new GeoPosition(lat, lon));
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object
                && TryNumber(item, "lat", out var objLat)
                && TryNumber(item, "lon", out var objLon))
            {
                if (objLat is < -90 or > 90 || !double.IsFinite(objLat))
                    return new InvalidArgument(name, $"lat {objLat} is outside [-90, 90]");
                if (objLon is < -180 or > 180 || !double.IsFinite(objLon))
                    return new InvalidArgument(name, $"lon {objLon} is outside [-180, 180]");
                positions.Add(new GeoPosition(objLat, objLon));
                continue;
            }

            return new InvalidArgument(name, "each point is a position string or {lat, lon}");
        }

        return null;
    }

    private static bool TryNumber(JsonElement item, string name, out double value)
    {
        value = 0;
        return item.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value);
    }
}

/// <summary>Wraps the measure tools as <see cref="McpServerTool"/>s.</summary>
internal static class MeasureMcpAdapters
{
    public static McpServerTool Create(GetMeasureTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetMeasureTool.Name,
            Description =
                "Returns the measure tool: whether it's on, the geometry (rhumb or greatCircle), the earth model, the " +
                "path's points, and per leg the from and to positions (decimal degrees and the app's text), the distance " +
                "in metres and NM and the forward and reverse true bearings, all unrounded; the total; and what the " +
                "measure card shows for the selected leg, rounded as displayed. Change it with set_measure.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SetMeasureTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The path's points in order, each a position string the card accepts (\"32°35.224'S 061°17.710'E\", \"-32.587, 61.295\", DMS, hemisphere letters either side) or {\"lat\": …, \"lon\": …} in decimal degrees. An empty array clears the path; omit to keep it.")] JsonElement? points = null,
            [Description("rhumb or greatCircle; omit to keep the current geometry. Remembered per user, as the card's control is.")] string? geometry = null,
            [Description("One-based leg the card shows; omit for the last leg.")] int? selectedLeg = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(points, geometry, selectedLeg, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetMeasureTool.Name,
            Description =
                "Turns the measure tool on and sets its path (finalised, as typed positions are), its geometry and the " +
                "leg its card shows, then returns what get_measure returns. Use it to run the S-164 accuracy tests from " +
                "typed positions; frame the result with set_viewport for a capture. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
