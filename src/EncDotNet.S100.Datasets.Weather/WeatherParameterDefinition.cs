using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// One row of the canonical weather parameter table (design note
/// weather-sources.md §3.5): a canonical id, its stored unit, and the GRIB2
/// codes and CF standard names readers map to it. The rows are listed in
/// <see cref="WeatherParameters.All"/>.
/// </summary>
public sealed class WeatherParameterDefinition
{
    /// <summary>Creates a parameter definition.</summary>
    /// <param name="id">The canonical id, for example <c>"wind.u"</c>.</param>
    /// <param name="name">A short display name.</param>
    /// <param name="unit">The unit values are stored in.</param>
    /// <param name="group">The id of the group drawn together; see <see cref="Group"/>.</param>
    /// <param name="level">The level the id refers to, or <see langword="null"/> for any level.</param>
    /// <param name="gribCodes">The GRIB2 codes, in order of preference.</param>
    /// <param name="cfStandardNames">The CF standard names, in order of preference.</param>
    internal WeatherParameterDefinition(
        string id,
        string name,
        WeatherUnit unit,
        string group,
        WeatherLevel? level,
        IReadOnlyList<GribParameterCode> gribCodes,
        IReadOnlyList<string> cfStandardNames)
    {
        Id = id;
        Name = name;
        Unit = unit;
        Group = group;
        Level = level;
        GribCodes = gribCodes;
        CfStandardNames = cfStandardNames;
    }

    /// <summary>The canonical id, for example <c>"wind.u"</c> or <c>"wave.hs"</c>.</summary>
    public string Id { get; }

    /// <summary>A short display name, for example <c>"Significant wave height"</c>.</summary>
    public string Name { get; }

    /// <summary>The unit values are stored in. Readers convert to it.</summary>
    public WeatherUnit Unit { get; }

    /// <summary>
    /// The id of the parameter group this parameter is drawn with, for
    /// example <c>"wind"</c> for the U and V components, speed and
    /// direction. A <see cref="WeatherCoverageSource"/> serves one group.
    /// </summary>
    public string Group { get; }

    /// <summary>
    /// The level the canonical id refers to (10 m above ground for the wind
    /// components, speed and direction; mean sea level for pressure), or
    /// <see langword="null"/> when the id applies at whatever level the source
    /// gives. A reader maps a variable to this id only at this level; the same
    /// variable at another level is an unknown parameter.
    /// </summary>
    public WeatherLevel? Level { get; }

    /// <summary>
    /// The GRIB2 (discipline, category, number) codes for this parameter
    /// (WMO code table 4.2), in order of preference: when a file holds more
    /// than one, a reader keeps the first.
    /// </summary>
    public IReadOnlyList<GribParameterCode> GribCodes { get; }

    /// <summary>
    /// The CF standard names for this parameter (CF standard name table), in
    /// order of preference: when a file holds more than one, a reader keeps
    /// the first.
    /// </summary>
    public IReadOnlyList<string> CfStandardNames { get; }

    /// <summary>
    /// Reads a stored value as a <see cref="Speed"/>. Only for parameters in
    /// <see cref="WeatherUnit.MetresPerSecond"/>.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns>The speed, or <see langword="null"/> when <paramref name="value"/> is NaN (missing).</returns>
    /// <exception cref="InvalidOperationException">The parameter isn't stored in metres per second.</exception>
    public Speed? ToSpeed(float value)
    {
        RequireUnit(WeatherUnit.MetresPerSecond, nameof(Speed));
        return float.IsNaN(value) ? null : Speed.FromMetresPerSecond(value);
    }

    /// <summary>
    /// Reads a stored value as a <see cref="Length"/>. Only for parameters in
    /// <see cref="WeatherUnit.Metres"/>.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns>The length, or <see langword="null"/> when <paramref name="value"/> is NaN (missing).</returns>
    /// <exception cref="InvalidOperationException">The parameter isn't stored in metres.</exception>
    public Length? ToLength(float value)
    {
        RequireUnit(WeatherUnit.Metres, nameof(Length));
        return float.IsNaN(value) ? null : Length.FromMetres(value);
    }

    /// <summary>
    /// Reads a stored value as an <see cref="Angle"/> clockwise from true
    /// north. Only for parameters in <see cref="WeatherUnit.DegreesTrue"/>.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns>The angle, or <see langword="null"/> when <paramref name="value"/> is NaN (missing).</returns>
    /// <exception cref="InvalidOperationException">The parameter isn't stored in degrees true.</exception>
    public Angle? ToAngle(float value)
    {
        RequireUnit(WeatherUnit.DegreesTrue, nameof(Angle));
        return float.IsNaN(value) ? null : Angle.FromDegrees(value);
    }

    /// <summary>
    /// Reads a stored value as a period. Only for parameters in
    /// <see cref="WeatherUnit.Seconds"/>.
    /// </summary>
    /// <param name="value">A stored value.</param>
    /// <returns>The period, or <see langword="null"/> when <paramref name="value"/> is NaN (missing).</returns>
    /// <exception cref="InvalidOperationException">The parameter isn't stored in seconds.</exception>
    public TimeSpan? ToPeriod(float value)
    {
        RequireUnit(WeatherUnit.Seconds, nameof(TimeSpan));
        return float.IsNaN(value) ? null : TimeSpan.FromSeconds(value);
    }

    /// <summary>Returns <see cref="Id"/>.</summary>
    public override string ToString() => Id;

    private void RequireUnit(WeatherUnit expected, string quantity)
    {
        if (Unit != expected)
        {
            throw new InvalidOperationException(
                $"'{Id}' is stored in {WeatherUnits.GetLabel(Unit)}, so its values can't be read as a {quantity}.");
        }
    }
}
