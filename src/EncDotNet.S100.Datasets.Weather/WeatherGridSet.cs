namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The in-memory model every weather reader produces (design note
/// weather-sources.md §3.4): where the data comes from, one regular
/// latitude/longitude grid, and the parameters on it, each with its time
/// steps. Everything after the reader (normalisation, drawing, sampling and
/// picking) works on this model.
/// </summary>
/// <remarks>
/// Weather model data isn't official S-100 or ENC data and isn't for
/// navigation; hosts label it so wherever it appears (§3.1).
/// </remarks>
public sealed class WeatherGridSet
{
    /// <summary>Creates a grid set.</summary>
    /// <param name="origin">The model or centre and the run time.</param>
    /// <param name="nativeGrid">The grid as the file stores it; it's normalised here.</param>
    /// <param name="parameters">The parameters on the grid.</param>
    /// <exception cref="UnsupportedWeatherGridException">
    /// The grid can't be normalised, for example because it crosses the
    /// antimeridian (see <see cref="WeatherGridLayout"/>).
    /// </exception>
    /// <exception cref="ArgumentException">Two parameters share an id and level.</exception>
    public WeatherGridSet(WeatherOrigin origin, WeatherNativeGrid nativeGrid, IReadOnlyList<WeatherParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(nativeGrid);
        ArgumentNullException.ThrowIfNull(parameters);

        var seen = new HashSet<(string, WeatherLevel)>();
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter, nameof(parameters));
            if (!seen.Add((parameter.Id, parameter.Level)))
            {
                throw new ArgumentException(
                    $"The grid set has two '{parameter.Id}' parameters at {parameter.Level.Description}.",
                    nameof(parameters));
            }
        }

        Origin = origin;
        Layout = WeatherGridLayout.Create(nativeGrid);
        Parameters = parameters.ToArray();
    }

    /// <summary>The model or centre and the run time.</summary>
    public WeatherOrigin Origin { get; }

    /// <summary>How the file's grid maps onto <see cref="Grid"/>.</summary>
    public WeatherGridLayout Layout { get; }

    /// <summary>The normalised grid: south-west origin, rows running north, longitudes in −180 to 180.</summary>
    public WeatherGrid Grid => Layout.Grid;

    /// <summary>The parameters on the grid, known and unknown, in the order the reader found them.</summary>
    public IReadOnlyList<WeatherParameter> Parameters { get; }

    /// <summary>Finds a parameter by id, at <paramref name="level"/> or at any level.</summary>
    /// <param name="id">The parameter id, for example <see cref="WeatherParameters.WindU"/>.</param>
    /// <param name="level">The level, or <see langword="null"/> for the first parameter with the id at any level.</param>
    /// <returns>The parameter, or <see langword="null"/> when the set has none.</returns>
    public WeatherParameter? FindParameter(string id, WeatherLevel? level = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var parameter in Parameters)
        {
            if (string.Equals(parameter.Id, id, StringComparison.Ordinal) &&
                (level is null || parameter.Level == level))
            {
                return parameter;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes one step of a parameter and normalises it to <see cref="Grid"/>'s
    /// order, with missing values as <see cref="float.NaN"/>. Nothing is
    /// cached; <see cref="WeatherCoverageSource"/> keeps recent steps.
    /// </summary>
    /// <param name="parameter">A parameter of this set.</param>
    /// <param name="stepIndex">Index into <see cref="WeatherParameter.Steps"/>.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the step.</param>
    /// <returns>A new array of length <see cref="WeatherGrid.CellCount"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stepIndex"/> is out of range.</exception>
    public float[] ReadStep(WeatherParameter parameter, int stepIndex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentOutOfRangeException.ThrowIfNegative(stepIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stepIndex, parameter.Steps.Count);

        var nativeValues = parameter.Steps[stepIndex].Decode(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Layout.Normalize(nativeValues, parameter.MissingValues);
    }
}
