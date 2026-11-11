using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// A NetCDF-CF wind and wave file read into a <see cref="WeatherGridSet"/>:
/// the grid, the parameters the weather table knows (and the ones it
/// doesn't), their time steps, and where the data comes from. Time steps are
/// read from the file only when they're decoded, so the dataset keeps the
/// file open until it's disposed.
/// </summary>
/// <remarks>
/// <para>How the file is read (design note weather-sources.md §7.1):</para>
/// <list type="bullet">
/// <item><description>
/// Latitude, longitude and time are found by <c>standard_name</c>, by
/// <c>axis</c> (<c>Y</c>, <c>X</c>, <c>T</c>), or by their units
/// (<c>degrees_north</c>, <c>degrees_east</c>, "&lt;unit&gt; since …"). They
/// must be one-dimensional and evenly spaced; 2-D latitude and longitude
/// (a curvilinear grid) or a <c>grid_mapping</c> other than
/// <c>latitude_longitude</c> fails with an
/// <see cref="UnsupportedWeatherGridException"/> naming the grid type.
/// </description></item>
/// <item><description>
/// Data variables are matched to the weather parameter table by their
/// <c>standard_name</c> (<see cref="WeatherParameters.FindByCfStandardName"/>)
/// and converted to its stored units. A variable may have extra dimensions of
/// length 1 (a single height or depth); a 10 m height is read as the wind
/// level. When two variables map to the same parameter, the one whose
/// standard name the table prefers is kept and the other is listed as unknown.
/// </description></item>
/// <item><description>
/// Values have <c>_FillValue</c>, <c>missing_value</c>, <c>valid_min</c>,
/// <c>valid_max</c> and <c>valid_range</c> applied to the stored values (and
/// the NetCDF default fill value when there's no <c>_FillValue</c>), become
/// <see cref="float.NaN"/> where missing, and are then unpacked with
/// <c>scale_factor</c> and <c>add_offset</c>.
/// </description></item>
/// <item><description>
/// The model is the global <c>source</c>, <c>institution</c> or <c>title</c>
/// attribute, in that order; the run time is a <c>forecast_reference_time</c>
/// variable or global attribute when there is one.
/// </description></item>
/// </list>
/// <para>A file with no recognisable coordinates, or no variable the table knows, fails with a
/// <see cref="CfConventionsException"/> that lists the variables found.</para>
/// </remarks>
public sealed class NetCdfWeatherDataset : IDisposable
{
    private NetCdfWeatherDataset(
        NetCdfFile file,
        WeatherGridSet gridSet,
        IReadOnlyList<DateTime> times,
        DateTime? referenceTime,
        IReadOnlyList<NetCdfIgnoredVariable> ignored)
    {
        File = file;
        GridSet = gridSet;
        Times = times;
        ReferenceTime = referenceTime;
        IgnoredVariables = ignored;
    }

    /// <summary>The open NetCDF file the steps are read from.</summary>
    public NetCdfFile File { get; }

    /// <summary>The file's format.</summary>
    public NetCdfFormat Format => File.Format;

    /// <summary>The grid set: origin, grid and parameters with lazily read time steps.</summary>
    public WeatherGridSet GridSet { get; }

    /// <summary>The valid times of the time coordinate, in file order.</summary>
    public IReadOnlyList<DateTime> Times { get; }

    /// <summary>
    /// The model run's reference time (UTC), from a <c>forecast_reference_time</c>
    /// variable or global attribute, or <see langword="null"/> when the file
    /// doesn't give one (<see cref="WeatherOrigin.RunTime"/> is then the first
    /// valid time).
    /// </summary>
    public DateTime? ReferenceTime { get; }

    /// <summary>The global <c>title</c> attribute, or <see langword="null"/>.</summary>
    public string? Title => File.GetGlobalText("title");

    /// <summary>The global <c>source</c> attribute (the model, in CF), or <see langword="null"/>.</summary>
    public string? Source => File.GetGlobalText("source");

    /// <summary>The global <c>institution</c> attribute, or <see langword="null"/>.</summary>
    public string? Institution => File.GetGlobalText("institution");

    /// <summary>The global <c>Conventions</c> attribute, for example <c>"CF-1.6"</c>, or <see langword="null"/>.</summary>
    public string? Conventions => File.GetGlobalText("Conventions");

    /// <summary>Data variables left out of the grid set, with why.</summary>
    public IReadOnlyList<NetCdfIgnoredVariable> IgnoredVariables { get; }

    /// <summary>Opens and reads the NetCDF file at <paramref name="path"/>.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The dataset; dispose it to close the file.</returns>
    /// <exception cref="NetCdfFormatException">The file isn't a NetCDF file this library can read.</exception>
    /// <exception cref="CfConventionsException">The file doesn't follow the CF conventions this reader needs.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid isn't a regular latitude/longitude grid.</exception>
    public static NetCdfWeatherDataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(NetCdfFile.Open(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Opens and reads a NetCDF file from a seekable stream.</summary>
    /// <param name="stream">The file's bytes; readable and seekable.</param>
    /// <param name="name">The file's name, used as the model name when the file names none.</param>
    /// <param name="leaveOpen">Whether disposing the dataset leaves <paramref name="stream"/> open.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="NetCdfFormatException">The bytes aren't a NetCDF file this library can read.</exception>
    /// <exception cref="CfConventionsException">The file doesn't follow the CF conventions this reader needs.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid isn't a regular latitude/longitude grid.</exception>
    public static NetCdfWeatherDataset Open(Stream stream, string? name = null, bool leaveOpen = false) =>
        Read(NetCdfFile.Open(stream, leaveOpen), name);

    /// <summary>
    /// Reads an open NetCDF file as CF weather data. The dataset takes
    /// ownership of <paramref name="file"/>, and disposes it if reading fails.
    /// </summary>
    /// <param name="file">The open file.</param>
    /// <param name="name">The file's name, used as the model name when the file names none.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="CfConventionsException">The file doesn't follow the CF conventions this reader needs.</exception>
    /// <exception cref="UnsupportedWeatherGridException">The grid isn't a regular latitude/longitude grid.</exception>
    public static NetCdfWeatherDataset Read(NetCdfFile file, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            var result = CfWeatherReader.Read(file, name);
            return new NetCdfWeatherDataset(file, result.GridSet, result.Times, result.ReferenceTime, result.Ignored);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Closes the file.</summary>
    public void Dispose() => File.Dispose();
}
