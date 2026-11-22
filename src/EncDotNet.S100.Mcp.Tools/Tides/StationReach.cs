using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>
/// sample tried a tide or tidal current prediction and no station reaches the
/// point (#1627): a few kilometres from a current station, this is the usual
/// answer, and it isn't the same as nothing being loaded.
/// </summary>
/// <param name="Quantity">The quantity asked for: <c>water_level</c> or <c>current</c>.</param>
/// <param name="Latitude">Latitude asked about.</param>
/// <param name="Longitude">Longitude asked about.</param>
/// <param name="ReachMeters">How far a station of this kind reaches, in metres.</param>
/// <param name="NearestStation">The nearest station that can be predicted, beyond the reach.</param>
[Description("Raised by sample when no S-104 or S-111 covers the point, predictions were tried, and no tide or current station is within reach. 'nearestStation' names the nearest one and how far it is.")]
public sealed record NoStationWithinReach(
    [property: Description("The quantity asked for: \"water_level\" or \"current\".")] string Quantity,
    [property: Description("Latitude asked about.")] double Latitude,
    [property: Description("Longitude asked about.")] double Longitude,
    [property: Description("How far a station of this kind reaches, in metres: a prediction is used only within it.")] double ReachMeters,
    [property: Description("The nearest station that can be predicted, beyond the reach.")] NearestStationBeyondReach NearestStation)
    : ToolError(
        "no_station_within_reach",
        Describe(Quantity, Latitude, Longitude, ReachMeters, NearestStation))
{
    private static string Describe(string quantity, double latitude, double longitude, double reach, NearestStationBeyondReach station)
    {
        var kind = quantity == "current" ? "current" : "tide";
        return FormattableString.Invariant(
            $"No {kind} station is within reach of {latitude:0.#####}, {longitude:0.#####}: the nearest, {station.Name ?? station.Id} ({station.Id}), is {StationReach.Distance(station.DistanceMeters)} away{(station.DistanceByWater ? " by water" : string.Empty)}, beyond the {StationReach.Distance(reach)} a {kind} station reaches. Load an {(kind == "current" ? "S-111" : "S-104")} dataset that covers the point, or predict at the station with predict (kind {kind}).");
    }
}

/// <summary>The nearest station beyond the reach, as <see cref="NoStationWithinReach"/> reports it.</summary>
/// <param name="Id">The station's id.</param>
/// <param name="Name">The station's name.</param>
/// <param name="Latitude">The station's latitude.</param>
/// <param name="Longitude">The station's longitude.</param>
/// <param name="DistanceMeters">How far the station is, in metres.</param>
/// <param name="DistanceByWater">Whether the distance goes round charted land.</param>
[Description("The nearest station that can be predicted, beyond the reach.")]
public sealed record NearestStationBeyondReach(
    [property: Description("The station's id; pass it to predict.")] string Id,
    [property: Description("The station's name.")] string? Name,
    [property: Description("The station's latitude.")] double Latitude,
    [property: Description("The station's longitude.")] double Longitude,
    [property: Description("How far the station is from the point, in metres.")] double DistanceMeters,
    [property: Description("True when the distance goes round the loaded charts' land; false when it's a straight line (no charts loaded, or the station is too far to measure by water).")] bool DistanceByWater);

/// <summary>
/// Finds the nearest predictable station beyond a reach, measuring by water
/// out to <see cref="MeasureMeters"/> and in a straight line past that.
/// </summary>
internal sealed class StationReach
{
    /// <summary>How far a distance is measured by water; farther stations get a straight-line distance.</summary>
    public const double MeasureMeters = 20_000;

    // Straight-line candidates looked at for the nearest by water.
    private const int Candidates = 8;

    private Measure? _measure;

    /// <summary>The nearest station that <paramref name="predictable"/> accepts.</summary>
    /// <param name="mask">The loaded charts' water, or <c>null</c> to measure in straight lines.</param>
    /// <param name="position">The point.</param>
    /// <param name="nearestFirst">Candidate stations, nearest first in a straight line, with their straight distances.</param>
    /// <param name="predictable">Whether a station can be predicted.</param>
    /// <returns>The nearest station, or <c>null</c> when there is none.</returns>
    public NearestStationBeyondReach? Nearest(
        IWaterMask? mask,
        GeoPosition position,
        IEnumerable<(string Id, string? Name, GeoPosition Position, double Straight)> nearestFirst,
        Func<string, bool> predictable)
    {
        var reach = Reach(mask);
        NearestStationBeyondReach? best = null;
        var looked = 0;
        foreach (var (id, name, at, straight) in nearestFirst)
        {
            if (best is not null && straight >= best.DistanceMeters)
            {
                // The distance by water is never shorter than the straight one.
                break;
            }

            if (!predictable(id))
            {
                continue;
            }

            var byWater = reach.ByWater ? reach.DistanceMeters(at, position) : null;
            var candidate = new NearestStationBeyondReach(
                id,
                name,
                at.Latitude,
                at.Longitude,
                Math.Round(byWater ?? straight),
                byWater is not null);

            // A straight distance stands in when land cuts the station off within the measure;
            // it's shorter than the real one, so a station measured by water wins a tie.
            if (best is null || candidate.DistanceMeters < best.DistanceMeters)
            {
                best = candidate;
            }

            if (++looked >= Candidates)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>A distance as the messages write it: metres under a kilometre, else kilometres.</summary>
    internal static string Distance(double meters) => meters < 1_000
        ? FormattableString.Invariant($"{Math.Round(meters):0} m")
        : FormattableString.Invariant($"{meters / 1_000:0.#} km");

    // One reach per mask, so a polyline's samples share the distance fields.
    private WaterReach Reach(IWaterMask? mask)
    {
        var current = _measure;
        if (current is not null && ReferenceEquals(current.Mask, mask))
        {
            return current.Reach;
        }

        var fresh = new Measure(mask, new WaterReach(mask, MeasureMeters));
        _measure = fresh;
        return fresh.Reach;
    }

    private sealed record Measure(IWaterMask? Mask, WaterReach Reach);
}
