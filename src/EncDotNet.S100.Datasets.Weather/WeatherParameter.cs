namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// One variable at one level of a <see cref="WeatherGridSet"/>, with its time
/// steps: for example the U component of the wind at 10 m.
/// </summary>
public sealed class WeatherParameter
{
    /// <summary>Creates a parameter from a row of the canonical table.</summary>
    /// <param name="definition">The canonical parameter, from <see cref="WeatherParameters"/>.</param>
    /// <param name="level">The level the values are given at.</param>
    /// <param name="steps">The time steps.</param>
    /// <param name="missingValues">
    /// Sentinel values that mark missing points in decoded steps (for example a
    /// CF <c>_FillValue</c>). NaN is always missing.
    /// </param>
    /// <param name="sourceName">The reader's name for the variable, for example a GRIB2 short name or a NetCDF variable name.</param>
    public WeatherParameter(
        WeatherParameterDefinition definition,
        WeatherLevel level,
        IReadOnlyList<WeatherTimeStep> steps,
        IReadOnlyList<float>? missingValues = null,
        string? sourceName = null)
        : this(
            (definition ?? throw new ArgumentNullException(nameof(definition))).Id,
            definition,
            definition.Unit,
            WeatherUnits.GetLabel(definition.Unit),
            level,
            steps,
            missingValues,
            sourceName)
    {
    }

    /// <summary>
    /// Creates a parameter that isn't in the canonical table. It's listed (for
    /// example in <c>info</c> output) but not drawn.
    /// </summary>
    /// <param name="id">
    /// An id that can't clash with a canonical one, for example
    /// <c>"grib2:0,1,8"</c> or <c>"netcdf:tp"</c>.
    /// </param>
    /// <param name="units">The units as the file gives them.</param>
    /// <param name="level">The level the values are given at.</param>
    /// <param name="steps">The time steps.</param>
    /// <param name="missingValues">Sentinel values that mark missing points in decoded steps.</param>
    /// <param name="sourceName">The reader's name for the variable.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or is a canonical id.</exception>
    public WeatherParameter(
        string id,
        string units,
        WeatherLevel level,
        IReadOnlyList<WeatherTimeStep> steps,
        IReadOnlyList<float>? missingValues = null,
        string? sourceName = null)
        : this(
            RequireUnknownId(id),
            null,
            WeatherUnit.Other,
            units ?? throw new ArgumentNullException(nameof(units)),
            level,
            steps,
            missingValues,
            sourceName)
    {
    }

    private WeatherParameter(
        string id,
        WeatherParameterDefinition? definition,
        WeatherUnit unit,
        string units,
        WeatherLevel level,
        IReadOnlyList<WeatherTimeStep> steps,
        IReadOnlyList<float>? missingValues,
        string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(steps);
        Id = id;
        Definition = definition;
        Unit = unit;
        Units = units;
        Level = level;
        Steps = steps.ToArray();
        MissingValues = missingValues?.ToArray() ?? [];
        SourceName = sourceName;
    }

    /// <summary>The canonical id (<see cref="WeatherParameters"/>), or the reader's id for an unknown parameter.</summary>
    public string Id { get; }

    /// <summary>The row of the canonical table, or <see langword="null"/> for an unknown parameter.</summary>
    public WeatherParameterDefinition? Definition { get; }

    /// <summary>Whether the parameter is in the canonical table, and so can be drawn.</summary>
    public bool IsKnown => Definition is not null;

    /// <summary>The unit values are stored in; <see cref="WeatherUnit.Other"/> for an unknown parameter.</summary>
    public WeatherUnit Unit { get; }

    /// <summary>The units label, for example <c>"metres"</c>, or the file's units for an unknown parameter.</summary>
    public string Units { get; }

    /// <summary>The level the values are given at.</summary>
    public WeatherLevel Level { get; }

    /// <summary>The level description, for example <c>"10 m above ground"</c>.</summary>
    public string LevelDescription => Level.Description;

    /// <summary>The time steps, in the order the reader found them.</summary>
    public IReadOnlyList<WeatherTimeStep> Steps { get; }

    /// <summary>Sentinel values that mark missing points in decoded steps. NaN is always missing.</summary>
    public IReadOnlyList<float> MissingValues { get; }

    /// <summary>The reader's name for the variable, when it has one.</summary>
    public string? SourceName { get; }

    /// <summary>Returns the id and level, for example <c>"wind.u @ 10 m above ground"</c>.</summary>
    public override string ToString() => $"{Id} @ {Level.Description}";

    private static string RequireUnknownId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (WeatherParameters.Find(id) is not null)
        {
            throw new ArgumentException(
                $"'{id}' is a canonical parameter id; create the parameter from its WeatherParameterDefinition instead.",
                nameof(id));
        }

        return id;
    }
}
