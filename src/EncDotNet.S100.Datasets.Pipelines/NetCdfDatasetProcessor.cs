using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.NetCdf;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for NetCDF-CF wind and wave grids: NetCDF-4 (on PureHDF)
/// and NetCDF-3 (classic and 64-bit offset, the format of ERDDAP's
/// <c>.nc</c> output), read by <see cref="NetCdfWeatherDataset"/> and drawn,
/// sampled and picked by <see cref="WeatherGridProcessorBase"/>. Weather model
/// data isn't an S-100 product and isn't for navigation.
/// </summary>
/// <remarks>
/// <para>
/// The processor reports <c>NetCDF/4.0.0</c> for a NetCDF-4 file and
/// <c>NetCDF/3.0.0</c> for a NetCDF-3 one. The file stays open and each time
/// step is read when it's first drawn or sampled, so dispose the processor to
/// close it.
/// </para>
/// <para>
/// A file the reader can't use fails at construction: a
/// <see cref="NetCdfFormatException"/> (not NetCDF, CDF-5, an unsupported
/// compression filter), a <see cref="CfConventionsException"/> (no CF
/// coordinates or known variables, listing what the file holds; unsupported
/// time units or calendar) or an <see cref="UnsupportedWeatherGridException"/>
/// (a curvilinear, projected or irregular grid). Each message starts with the
/// file name.
/// </para>
/// </remarks>
public sealed class NetCdfDatasetProcessor : WeatherGridProcessorBase
{
    /// <summary>The spec name NetCDF files are registered under.</summary>
    public const string SpecName = "NetCDF";

    private readonly NetCdfWeatherDataset _dataset;

    /// <summary>Opens the NetCDF-CF file at <paramref name="path"/>.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="crsTransformFactory">Creates the transforms the headless compositor needs.</param>
    public NetCdfDatasetProcessor(string path, ICrsTransformFactory crsTransformFactory)
        : this(Open(File.OpenRead(path), Path.GetFileName(path)), crsTransformFactory)
    {
    }

    /// <summary>Opens the NetCDF-CF file <paramref name="relativePath"/> in <paramref name="source"/>.</summary>
    /// <param name="source">The asset source holding the file.</param>
    /// <param name="relativePath">The file's source-relative path.</param>
    /// <param name="crsTransformFactory">Creates the transforms the headless compositor needs.</param>
    public NetCdfDatasetProcessor(IAssetSource source, string relativePath, ICrsTransformFactory crsTransformFactory)
        : this(
            Open(AssetSourceHelpers.OpenSeekable(source, relativePath), AssetSourceHelpers.GetFileName(relativePath)),
            crsTransformFactory)
    {
    }

    private NetCdfDatasetProcessor((NetCdfWeatherDataset Dataset, string FileName, string Hash) opened, ICrsTransformFactory crsTransformFactory)
        : base(
            opened.Dataset.GridSet,
            SpecFor(opened.Dataset.Format),
            opened.FileName,
            opened.Dataset.ReferenceTime,
            crsTransformFactory,
            opened.Hash)
    {
        _dataset = opened.Dataset;
    }

    /// <summary>The NetCDF file read as CF weather data.</summary>
    public NetCdfWeatherDataset Dataset => _dataset;

    /// <summary>The spec a NetCDF format reports: <c>NetCDF/4.0.0</c> or <c>NetCDF/3.0.0</c>.</summary>
    /// <param name="format">The file's format.</param>
    public static SpecRef SpecFor(NetCdfFormat format) =>
        SpecRef.Parse(format == NetCdfFormat.NetCdf4 ? "NetCDF/4.0.0" : "NetCDF/3.0.0");

    /// <summary>
    /// Reads the dataset metadata of the NetCDF-CF file at <paramref name="path"/>
    /// (spec, extent, CRS and time coverage) without decoding any time step.
    /// </summary>
    /// <param name="path">The file's path.</param>
    public static DatasetMetadata ReadMetadata(string path)
    {
        NetCdfWeatherDataset dataset;
        try
        {
            dataset = NetCdfWeatherDataset.Open(path);
        }
        catch (Exception ex)
        {
            var wrapped = WithFile(ex, Path.GetFileName(path));
            if (ReferenceEquals(wrapped, ex))
                throw;
            throw wrapped;
        }

        using (dataset)
            return BuildMetadata(dataset.GridSet, SpecFor(dataset.Format), dataset.Times);
    }

    /// <inheritdoc />
    /// <remarks>Adds the data variables left out of the grid set (<see cref="NetCdfWeatherDataset.IgnoredVariables"/>).</remarks>
    protected override IEnumerable<WeatherDatasetContent> DescribeContents() =>
        base.DescribeContents().Concat(
            _dataset.IgnoredVariables.Select(v => new WeatherDatasetContent(v.Name, string.Empty, 0, v.Reason)));

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => _dataset.Dispose();

    private static (NetCdfWeatherDataset Dataset, string FileName, string Hash) Open(Stream stream, string fileName)
    {
        try
        {
            var hash = DatasetContentHash.HashAndRewind(ref stream);
            return (NetCdfWeatherDataset.Open(stream, Path.GetFileNameWithoutExtension(fileName)), fileName, hash);
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

    // Prefixes the reader's message with the file name, keeping the type.
    internal static Exception WithFile(Exception exception, string fileName) => exception switch
    {
        NetCdfFormatException e => new NetCdfFormatException($"{fileName}: {e.Message}", e),
        CfConventionsException e => new CfConventionsException($"{fileName}: {e.Message}", e),
        UnsupportedWeatherGridException e => new UnsupportedWeatherGridException($"{fileName}: {e.Message}", e),
        _ => exception,
    };
}
