using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Tools;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>A position in a Range &amp; bearing result.</summary>
[Description("A position: decimal degrees and the app's text.")]
internal sealed record RangeBearingPositionDto(
    [property: Description("Latitude in decimal degrees, unrounded.")] double Lat,
    [property: Description("Longitude in decimal degrees, unrounded.")] double Lon,
    [property: Description("The position as the card shows it, degrees and decimal minutes.")] string Text);

/// <summary>The Range &amp; bearing tool's range rings.</summary>
[Description("The range rings: geodesic circles round the origin at a fixed interval.")]
internal sealed record RangeBearingRingsDto(
    [property: Description("True when the rings are drawn.")] bool Enabled,
    [property: Description("Distance between rings in metres.")] double IntervalMetres,
    [property: Description("How many rings.")] int Count,
    [property: Description("Each drawn ring's range in metres, innermost first; empty while the rings are off.")] IReadOnlyList<double> RangesMetres);

/// <summary>The Range &amp; bearing tool's state (#1130 D23).</summary>
[Description("The Range & bearing tool: origin, range, bearing, locks, end, reverse bearing, rings and the earth model. Values are unrounded.")]
internal sealed record RangeBearingDto(
    [property: Description("True while the Range & bearing map tool is on.")] bool Active,
    [property: Description("placing_origin (the next click sets the origin), following (the end follows the cursor) or fixed (the end is fixed).")] string Phase,
    [property: Description("rhumb or greatCircle: how the bearing line runs and how the end is computed. The rings are geodesic circles either way.")] string Geometry,
    [property: Description("The origin, or null before it's placed.")] RangeBearingPositionDto? Origin,
    [property: Description("The range in metres, or null.")] double? RangeMetres,
    [property: Description("The range in nautical miles, or null.")] double? RangeNauticalMiles,
    [property: Description("The initial true bearing at the origin in degrees, or null.")] double? BearingDegrees,
    [property: Description("True when the range was typed, so the cursor only swings the bearing (a VRM).")] bool RangeLocked,
    [property: Description("True when the bearing was typed, so the cursor only changes the range (an EBL).")] bool BearingLocked,
    [property: Description("The end: range from the origin on the bearing along the geometry; null until all three are known.")] RangeBearingPositionDto? End,
    [property: Description("The true bearing at the end back to the origin in degrees; on a great circle this isn't bearing + 180.")] double? ReverseBearingDegrees,
    [property: Description("The range rings.")] RangeBearingRingsDto Rings,
    [property: Description("The earth model, as the card's footer shows it: \"Sphere · R 6,371 km\".")] string EarthModel,
    [property: Description("The sphere's radius in metres.")] double EarthRadiusMetres);

/// <summary>Reads the Range &amp; bearing tool.</summary>
internal sealed class GetRangeBearingTool
{
    public const string Name = "get_range_bearing";

    private readonly RangeBearingState _state;
    private readonly IRouteEditInvoker _invoker;

    public GetRangeBearingTool(RangeBearingState state, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(invoker);
        _state = state;
        _invoker = invoker;
    }

    public Task<ToolResult<RangeBearingDto>> InvokeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<RangeBearingDto>.Ok(Dto(_state)));
    }

    internal static RangeBearingDto Dto(RangeBearingState state) => new(
        state.IsToolActive,
        state.Phase switch
        {
            RangeBearingState.RangeBearingPhase.Following => "following",
            RangeBearingState.RangeBearingPhase.Fixed => "fixed",
            _ => "placing_origin",
        },
        GeometryName(state.Geometry),
        Position(state.Origin),
        state.RangeMetres,
        state.RangeMetres / PassageGeodesy.MetersPerNauticalMile,
        state.BearingDegrees,
        state.IsRangeLocked,
        state.IsBearingLocked,
        Position(state.End),
        state.ReverseBearingDegrees,
        new RangeBearingRingsDto(state.RingsEnabled, state.RingIntervalMetres, state.RingCount, state.RingRanges),
        RangeBearingText.Model(),
        GeodesicHelper.EarthRadiusMetres);

    internal static string GeometryName(LegGeometry geometry) =>
        geometry == LegGeometry.GreatCircle ? "greatCircle" : "rhumb";

    private static RangeBearingPositionDto? Position(GeoPosition? p) =>
        p is { } q ? new RangeBearingPositionDto(q.Latitude, q.Longitude, LatLonFormatter.Format(q.Latitude, q.Longitude)) : null;
}

/// <summary>
/// Turns the Range &amp; bearing tool on and sets what it's given, locking a
/// given range or bearing as typing it on the card does.
/// </summary>
internal sealed class SetRangeBearingTool
{
    public const string Name = "set_range_bearing";

    private readonly RangeBearingState _state;
    private readonly ViewerSettings _settings;
    private readonly IRouteEditInvoker _invoker;

    public SetRangeBearingTool(RangeBearingState state, ViewerSettings settings, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(invoker);
        _state = state;
        _settings = settings;
        _invoker = invoker;
    }

    public Task<ToolResult<RangeBearingDto>> InvokeAsync(
        JsonElement? origin,
        JsonElement? range,
        double? bearing,
        string? geometry,
        JsonElement? rings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unit = Enum.TryParse<DistanceUnit>(_settings.DistanceUnit, ignoreCase: true, out var u) ? u : DistanceUnit.NauticalMiles;

        // Validate everything before changing anything.
        GeoPosition? originPosition = null;
        if (IsGiven(origin) && !TryPosition(origin!.Value, out originPosition, out var originError))
            return Err(new InvalidArgument("origin", originError));

        double? rangeMetres = null;
        if (IsGiven(range) && !TryRange(range!.Value, unit, out rangeMetres, out var rangeError))
            return Err(new InvalidArgument("range", rangeError));

        if (bearing is { } b && (!double.IsFinite(b) || b < 0 || b > 360))
            return Err(new InvalidArgument("bearing", $"value {b} must be from 0 to 360 degrees true"));

        LegGeometry? legGeometry = null;
        if (geometry is not null)
        {
            legGeometry = geometry.Trim().ToLowerInvariant() switch
            {
                "rhumb" => LegGeometry.Rhumb,
                "greatcircle" or "great_circle" => LegGeometry.GreatCircle,
                _ => null,
            };
            if (legGeometry is null)
                return Err(new InvalidArgument("geometry", $"'{geometry}' must be rhumb or greatCircle"));
        }

        (bool Enabled, double? Interval, int? Count)? ringRequest = null;
        if (IsGiven(rings))
        {
            if (!TryRings(rings!.Value, unit, out var r, out var ringError))
                return Err(new InvalidArgument("rings", ringError));
            ringRequest = r;
        }

        return _invoker.InvokeAsync(() =>
        {
            _state.IsToolActive = true;
            if (legGeometry is { } g)
                _state.Geometry = g;
            if (rangeMetres is { } m)
                _state.SetRange(m);
            if (bearing is { } deg)
                _state.SetBearing(deg);
            if (originPosition is { } o)
                _state.SetOrigin(o);
            if (ringRequest is { } rr)
                _state.SetRings(rr.Enabled, rr.Interval, rr.Count);
            return ToolResult<RangeBearingDto>.Ok(GetRangeBearingTool.Dto(_state));
        });
    }

    private static bool IsGiven(JsonElement? e) =>
        e is { } v && v.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    internal static bool TryPosition(JsonElement e, out GeoPosition? position, out string error)
    {
        position = null;
        error = string.Empty;
        double lat, lon;
        if (e.ValueKind == JsonValueKind.String)
        {
            if (!LatLonFormatter.TryParse(e.GetString(), out lat, out lon))
            {
                error = $"'{e.GetString()}' is not a position; try \"32°35.224'S 061°17.710'E\" or {{\"lat\": -32.587, \"lon\": 61.295}}";
                return false;
            }
        }
        else if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("lat", out var latElement) && latElement.TryGetDouble(out lat)
            && e.TryGetProperty("lon", out var lonElement) && lonElement.TryGetDouble(out lon))
        {
            if (lat is < -90 or > 90 || lon is < -180 or > 180 || !double.IsFinite(lat) || !double.IsFinite(lon))
            {
                error = $"({lat}, {lon}) is outside the WGS-84 range";
                return false;
            }
        }
        else
        {
            error = "must be a position string or an object {\"lat\": number, \"lon\": number}";
            return false;
        }

        position = new GeoPosition(lat, lon);
        return true;
    }

    /// <summary>A number is metres; a string is read as the card reads it (a bare number in the Distance unit).</summary>
    internal static bool TryRange(JsonElement e, DistanceUnit unit, out double? metres, out string error)
    {
        metres = null;
        error = string.Empty;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var value))
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                error = $"value {value} must be a positive number of metres";
                return false;
            }

            metres = value;
            return true;
        }

        if (e.ValueKind == JsonValueKind.String)
        {
            if (MeasureFormat.TryParseRange(e.GetString(), unit, out var parsed, out var parseError))
            {
                metres = parsed;
                return true;
            }

            error = parseError switch
            {
                RangeParseError.AmbiguousM => "a bare M is ambiguous (metres or nautical miles); use m or NM",
                RangeParseError.NotPositive => "the range must be more than zero",
                _ => $"'{e.GetString()}' is not a range; try 2000000 (metres), \"2,000 km\" or \"1079.9 NM\"",
            };
            return false;
        }

        error = "must be metres as a number, or a string with a unit";
        return false;
    }

    internal static bool TryRings(JsonElement e, DistanceUnit unit, out (bool Enabled, double? Interval, int? Count) rings, out string error)
    {
        rings = default;
        error = string.Empty;
        if (e.ValueKind is JsonValueKind.False or JsonValueKind.True)
        {
            rings = (e.GetBoolean(), null, null);
            return true;
        }

        if (e.ValueKind != JsonValueKind.Object)
        {
            error = "must be {\"interval\": metres or a string with a unit, \"count\": 1 to 20}, or false to turn the rings off";
            return false;
        }

        double? interval = null;
        if (e.TryGetProperty("interval", out var intervalElement))
        {
            if (!TryRange(intervalElement, unit, out interval, out var intervalError))
            {
                error = "interval " + intervalError;
                return false;
            }
        }

        int? count = null;
        if (e.TryGetProperty("count", out var countElement))
        {
            if (!countElement.TryGetInt32(out var n) || n is < 1 or > RangeBearingState.MaxRingCount)
            {
                error = string.Create(CultureInfo.InvariantCulture, $"count must be a whole number from 1 to {RangeBearingState.MaxRingCount}");
                return false;
            }

            count = n;
        }

        rings = (true, interval, count);
        return true;
    }

    private static Task<ToolResult<RangeBearingDto>> Err(ToolError error) =>
        Task.FromResult(ToolResult<RangeBearingDto>.Err(error));
}

/// <summary>Wraps the Range &amp; bearing tools as <see cref="McpServerTool"/>s.</summary>
internal static class RangeBearingMcpAdapters
{
    public static McpServerTool Create(GetRangeBearingTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetRangeBearingTool.Name,
            Description =
                "Returns the Range & bearing tool (an EBL/VRM pair from a chosen origin): whether it's on, the geometry " +
                "(rhumb or greatCircle), the origin, range (metres and NM), bearing, which of range and bearing are " +
                "locked, the end, the reverse bearing, the range rings and the earth model (a sphere). Values are " +
                "unrounded; the card shows them rounded. Change it with set_range_bearing.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SetRangeBearingTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The origin: a position string as the card accepts (\"32°35.224'S 061°17.710'E\", \"-32.587, 61.295\") or {\"lat\", \"lon\"} in decimal degrees. Omit to keep the current origin.")] JsonElement? origin = null,
            [Description("The range: metres as a number (2000000), or a string with a unit (\"2,000 km\", \"1079.9 NM\", \"1.2 mi\", \"500 m\"; a bare M is refused). A string without a unit is in the Distance unit setting. Locks the range. Omit to keep it.")] JsonElement? range = null,
            [Description("The initial true bearing at the origin, 0 to 360 degrees. Locks the bearing. Omit to keep it.")] double? bearing = null,
            [Description("rhumb or greatCircle: how the bearing line runs and how the end is computed. Remembered. Omit to keep it.")] string? geometry = null,
            [Description("Range rings: {\"interval\": metres or a string with a unit, \"count\": 1 to 20} turns them on (either field may be omitted to keep it); false turns them off.")] JsonElement? rings = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(origin, range, bearing, geometry, rings, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetRangeBearingTool.Name,
            Description =
                "Turns the Range & bearing map tool on (turning any other map tool off) and sets what it's given, " +
                "as typing on its card does: a given range or bearing is locked. With an origin, range and bearing " +
                "the end is fixed. Returns what get_range_bearing returns, unrounded. Frame the result with " +
                "set_viewport for a capture. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
