using System.Collections.ObjectModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// A station's tidal datums: the height of each above the station's own
/// reference (NOAA's station datum, STND), with the epoch they were computed
/// over and the date they were accepted.
/// </summary>
/// <remarks>
/// Harmonic constituents describe the tide about mean sea level (MSL), so
/// a prediction on another datum adds the height of MSL above it:
/// <see cref="TryGetTideDatum"/> gives that as a <see cref="TideDatum"/>.
/// The epoch and accepted date tell when a station's datums were revised:
/// NOAA is replacing the 1983–2001 National Tidal Datum Epoch with
/// 2002–2020, which moves most predictions on chart datum by a few
/// centimetres.
/// </remarks>
public sealed class StationDatums
{
    /// <summary>Creates a station's datums.</summary>
    /// <param name="heights">
    /// The height of each datum above the station's reference, in metres,
    /// by name (see <see cref="TideDatumNames"/>). Names are matched without
    /// regard to case.
    /// </param>
    /// <param name="epoch">The tidal datum epoch, such as <c>1983-2001</c>, or <c>null</c> when not given.</param>
    /// <param name="accepted">The date the datums were accepted, or <c>null</c> when not given.</param>
    /// <exception cref="ArgumentException">A height isn't a finite number.</exception>
    public StationDatums(IReadOnlyDictionary<string, double> heights, string? epoch, DateOnly? accepted)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var copy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in heights)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException($"The height of datum '{name}' isn't a finite number.", nameof(heights));
            }

            copy[name] = value;
        }

        Heights = new ReadOnlyDictionary<string, double>(copy);
        Epoch = epoch;
        Accepted = accepted;
    }

    /// <summary>The height of each datum above the station's reference, in metres, by name.</summary>
    public IReadOnlyDictionary<string, double> Heights { get; }

    /// <summary>The tidal datum epoch, such as <c>1983-2001</c>, or <c>null</c> when not given.</summary>
    public string? Epoch { get; }

    /// <summary>The date the datums were accepted: the station's revision date, or <c>null</c> when not given.</summary>
    public DateOnly? Accepted { get; }

    /// <summary>
    /// The datum to give a harmonic prediction on: the height of mean sea
    /// level above the named datum.
    /// </summary>
    /// <param name="name">The datum's name, such as <c>MLLW</c>.</param>
    /// <returns>
    /// The datum, or <c>null</c> when the station doesn't give it or doesn't
    /// give mean sea level.
    /// </returns>
    public TideDatum? TryGetTideDatum(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, TideDatumNames.MeanSeaLevel, StringComparison.OrdinalIgnoreCase))
        {
            return TideDatum.MeanSeaLevel;
        }

        return Heights.TryGetValue(TideDatumNames.MeanSeaLevel, out var msl) && Heights.TryGetValue(name, out var datum)
            ? new TideDatum(name.ToUpperInvariant(), msl - datum)
            : null;
    }
}
