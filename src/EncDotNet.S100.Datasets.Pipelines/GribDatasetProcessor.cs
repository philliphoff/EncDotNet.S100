using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Grib;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for GRIB2 wind and wave grids (NOAA GFS and GFS-Wave,
/// ECMWF open data), read by <see cref="GribWeatherDataset"/> and drawn,
/// sampled and picked by <see cref="WeatherGridProcessorBase"/>. Weather model
/// data isn't an S-100 product and isn't for navigation.
/// </summary>
/// <remarks>
/// <para>
/// The processor reports <c>GRIB2/2.0.0</c>. The file stays open and each
/// field is decoded only when its step is first drawn or sampled, so dispose
/// the processor to close it.
/// </para>
/// <para>
/// A file can hold many messages, or be several files appended (what the
/// NOMADS GRIB filter returns for several forecast hours). Fields the decoder
/// can't read (CCSDS, a projected grid), fields of an older run or
/// on another grid, and parameters the weather table doesn't know are listed
/// in <see cref="WeatherGridProcessorBase.Contents"/> with why, and not drawn.
/// </para>
/// <para>
/// A file the reader can't use fails at construction: a
/// <see cref="GribFormatException"/> (not GRIB2, truncated), an
/// <see cref="UnsupportedGribException"/> (GRIB edition 1, or no field it
/// can decode, naming the templates) or an
/// <see cref="UnsupportedWeatherGridException"/> (a regional grid across the
/// antimeridian). Each message starts with the file name.
/// </para>
/// </remarks>
public sealed class GribDatasetProcessor : WeatherGridProcessorBase
{
    /// <summary>The spec name GRIB2 files are registered under.</summary>
    public const string SpecName = Core.SpecName.Grib2;

    /// <summary>The spec every GRIB2 file reports: <c>GRIB2/2.0.0</c>.</summary>
    public static SpecRef Grib2Spec { get; } = SpecRef.Parse("GRIB2/2.0.0");

    private readonly GribWeatherDataset _dataset;

    /// <summary>
    /// Opens the GRIB2 file at <paramref name="path"/>, or, when it's a folder,
    /// the run its GRIB2 files hold (<see cref="GribRunFolder"/>, #1075): the
    /// files appended in name order, read as one dataset.
    /// </summary>
    /// <param name="path">The file's or folder's path.</param>
    /// <param name="crsTransformFactory">Creates the transforms the headless compositor needs.</param>
    public GribDatasetProcessor(string path, ICrsTransformFactory crsTransformFactory)
        : this(Directory.Exists(path) ? OpenFolder(path) : Open(File.OpenRead(path), Path.GetFileName(path)), crsTransformFactory)
    {
    }

    /// <summary>Opens the GRIB2 file <paramref name="relativePath"/> in <paramref name="source"/>.</summary>
    /// <param name="source">The asset source holding the file.</param>
    /// <param name="relativePath">The file's source-relative path.</param>
    /// <param name="crsTransformFactory">Creates the transforms the headless compositor needs.</param>
    /// <remarks>A folder in a <see cref="FileSystemAssetSource"/> is read as one run (<see cref="GribRunFolder"/>, #1075).</remarks>
    public GribDatasetProcessor(IAssetSource source, string relativePath, ICrsTransformFactory crsTransformFactory)
        : this(
            FolderIn(source, relativePath) is { } folder
                ? OpenFolder(folder)
                : Open(AssetSourceHelpers.OpenSeekable(source, relativePath), AssetSourceHelpers.GetFileName(relativePath)),
            crsTransformFactory)
    {
    }

    // The folder a file-system source's relative path names, if it is one.
    private static string? FolderIn(IAssetSource source, string relativePath)
    {
        if (source is not FileSystemAssetSource files)
            return null;
        var full = Path.GetFullPath(Path.Combine(files.RootPath, relativePath));
        return full.StartsWith(files.RootPath, StringComparison.Ordinal) && Directory.Exists(full) ? full : null;
    }

    private GribDatasetProcessor((GribWeatherDataset Dataset, string FileName, string Hash) opened, ICrsTransformFactory crsTransformFactory)
        : base(
            opened.Dataset.GridSet,
            Grib2Spec,
            opened.FileName,
            opened.Dataset.ReferenceTime,
            crsTransformFactory,
            opened.Hash)
    {
        _dataset = opened.Dataset;
    }

    /// <summary>The GRIB2 file read as weather data.</summary>
    public GribWeatherDataset Dataset => _dataset;

    /// <summary>
    /// Reads the dataset metadata of the GRIB2 file at <paramref name="path"/>
    /// (spec, extent, CRS and time coverage) without decoding any field.
    /// </summary>
    /// <param name="path">The file's path.</param>
    public static DatasetMetadata ReadMetadata(string path)
    {
        GribWeatherDataset dataset;
        try
        {
            dataset = Directory.Exists(path) ? GribWeatherDataset.OpenFolder(path) : GribWeatherDataset.Open(path);
        }
        catch (Exception ex)
        {
            var wrapped = WithFile(ex, Path.GetFileName(path));
            if (ReferenceEquals(wrapped, ex))
                throw;
            throw wrapped;
        }

        using (dataset)
            return BuildMetadata(dataset.GridSet, Grib2Spec, dataset.Times);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Adds why each unknown parameter isn't used (not in the table, a level
    /// the table doesn't use, a less preferred code, another swell partition)
    /// and the fields left out of the grid set.
    /// </remarks>
    protected override IEnumerable<WeatherDatasetContent> DescribeContents()
    {
        foreach (var parameter in GridSet.Parameters)
        {
            yield return Describe(
                parameter,
                parameter.IsKnown ? null : _dataset.UnusedReasons.GetValueOrDefault(parameter));
        }

        foreach (var ignored in _dataset.IgnoredFields)
            yield return new WeatherDatasetContent(ignored.Name, ignored.Field.ToWeatherLevel().Description, 0, ignored.Reason);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => _dataset.Dispose();

    private static (GribWeatherDataset Dataset, string FileName, string Hash) Open(Stream stream, string fileName)
    {
        try
        {
            var hash = DatasetContentHash.HashAndRewind(ref stream);
            return (GribWeatherDataset.Open(stream, fileName), fileName, hash);
        }
        catch (Exception ex)
        {
            stream.Dispose();
            var wrapped = WithFile(ex, fileName);
            if (ReferenceEquals(wrapped, ex))
                throw;
            throw wrapped;
        }
    }

    // A folder holding one run: its GRIB2 files read as one, hashed in order.
    private static (GribWeatherDataset Dataset, string FileName, string Hash) OpenFolder(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var files = GribRunFolder.Files(folder);
        string hash;
        var streams = new List<Stream>(files.Count);
        try
        {
            foreach (var file in files)
                streams.Add(File.OpenRead(file));
            hash = DatasetContentHash.HashAndRewind(streams);
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }

        try
        {
            return (GribWeatherDataset.OpenFolder(folder), name, hash);
        }
        catch (Exception ex)
        {
            var wrapped = WithFile(ex, name);
            if (ReferenceEquals(wrapped, ex))
                throw;
            throw wrapped;
        }
    }

    // Prefixes the reader's message with the file name, keeping the type.
    internal static Exception WithFile(Exception exception, string fileName) => exception switch
    {
        GribFormatException e => new GribFormatException($"{fileName}: {e.Message}", e),
        UnsupportedGribException e => new UnsupportedGribException($"{fileName}: {e.Message}", e),
        UnsupportedWeatherGridException e => new UnsupportedWeatherGridException($"{fileName}: {e.Message}", e),
        _ => exception,
    };
}
