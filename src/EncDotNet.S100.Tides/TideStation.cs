namespace EncDotNet.S100.Tides;

/// <summary>
/// A harmonic tide station: its identifier, its constituents and the datum
/// its predictions are given on.
/// </summary>
public sealed class TideStation
{
    /// <summary>Creates a station.</summary>
    /// <param name="id">The station identifier, such as NOAA's <c>9447130</c>.</param>
    /// <param name="name">The station's name, such as <c>Seattle</c>.</param>
    /// <param name="constituents">
    /// The harmonic constituents, with Greenwich phases. Every name must be
    /// one of <see cref="HarmonicConstituents.Names"/>, and no name may
    /// appear twice.
    /// </param>
    /// <param name="datum">
    /// The datum the predictions are given on. <see cref="TideDatum.MeanSeaLevel"/>
    /// gives heights about mean sea level.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A constituent's name is unknown or repeated, or its amplitude or phase
    /// isn't a finite number.
    /// </exception>
    public TideStation(string id, string name, IReadOnlyList<TideConstituent> constituents, TideDatum datum)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(constituents);
        ArgumentNullException.ThrowIfNull(datum);

        var seen = new HashSet<int>();
        foreach (var constituent in constituents)
        {
            ArgumentNullException.ThrowIfNull(constituent, nameof(constituents));
            var index = ConstituentTable.IndexOf(constituent.Name);
            if (index < 0)
            {
                throw new ArgumentException($"Unknown tidal constituent '{constituent.Name}'.", nameof(constituents));
            }

            if (!seen.Add(index))
            {
                throw new ArgumentException($"Tidal constituent '{constituent.Name}' appears more than once.", nameof(constituents));
            }

            if (!double.IsFinite(constituent.AmplitudeMeters) || !double.IsFinite(constituent.PhaseDegrees))
            {
                throw new ArgumentException($"Tidal constituent '{constituent.Name}' has a non-finite amplitude or phase.", nameof(constituents));
            }
        }

        if (!double.IsFinite(datum.MeanSeaLevelAboveDatumMeters))
        {
            throw new ArgumentException("The datum offset must be a finite number.", nameof(datum));
        }

        Id = id;
        Name = name;
        Constituents = [.. constituents];
        Datum = datum;
    }

    /// <summary>The station identifier.</summary>
    public string Id { get; }

    /// <summary>The station's name.</summary>
    public string Name { get; }

    /// <summary>The harmonic constituents, with Greenwich phases.</summary>
    public IReadOnlyList<TideConstituent> Constituents { get; }

    /// <summary>The datum the predictions are given on.</summary>
    public TideDatum Datum { get; }

    /// <summary>Returns a copy of the station with its predictions given on another datum.</summary>
    /// <param name="datum">The datum.</param>
    /// <returns>The station on <paramref name="datum"/>.</returns>
    public TideStation WithDatum(TideDatum datum) => new(Id, Name, Constituents, datum);
}
