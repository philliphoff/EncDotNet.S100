using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.TropicalCyclones;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for tropical cyclone advisories read from NHC's GIS
/// shapefile zips by <see cref="NhcAdvisoryReader"/> (design note
/// <c>docs/design/vector-weather.md</c> §8.1): the storm, the advisory, its
/// forecast points, track, cone and wind radii, and the past track. Tropical
/// cyclone advisories aren't S-100 products and aren't for navigation.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is the advisory's forecast zip (<c>{ATCF}_5day_{adv}.zip</c>),
/// read with the wind radii (<c>{ATCF}_fcst_{adv}.zip</c>) and best track
/// (<c>{ATCF}_best_track.zip</c>) zips beside it when they are there, or a
/// folder holding one storm's zips, whose latest advisory is read
/// (<see cref="NhcAdvisoryFiles"/>).
/// </para>
/// <para>
/// This processor reads and describes the advisory (<see cref="Advisory"/>,
/// <see cref="Metadata"/>). Drawing, picking and the advisory's time steps
/// come with the portrayal (issues #1296 and #1297).
/// </para>
/// </remarks>
public sealed class TropicalCycloneDatasetProcessor : IDatasetProcessor, ILoadedDatasetProjection
{
    /// <summary>The spec name tropical cyclone advisories are registered under.</summary>
    public const string SpecName = Core.SpecName.TropicalCyclone;

    /// <summary>The notice every report on a tropical cyclone advisory leads with.</summary>
    public const string NotForNavigationNotice = "Tropical cyclone advisory (NHC), not an S-100 product and not for navigation.";

    private readonly DatasetMetadata _metadata;

    /// <summary>Reads the advisory at <paramref name="path"/>: a forecast zip or a folder of one storm's zips.</summary>
    /// <param name="path">The forecast zip or folder.</param>
    /// <exception cref="NhcFormatException">The path isn't a readable NHC advisory.</exception>
    public TropicalCycloneDatasetProcessor(string path)
        : this(ReadNamed(path), Path.GetFileName(Path.TrimEndingDirectorySeparator(path)))
    {
    }

    /// <summary>
    /// Reads the forecast zip <paramref name="relativePath"/> from
    /// <paramref name="source"/>, with its wind radii and best track zips when
    /// the source has them beside it.
    /// </summary>
    /// <param name="source">The asset source holding the zips.</param>
    /// <param name="relativePath">The forecast zip's source-relative path.</param>
    /// <exception cref="NhcFormatException">The file isn't a readable NHC advisory.</exception>
    public TropicalCycloneDatasetProcessor(IAssetSource source, string relativePath)
        : this(ReadFromSource(source, relativePath), AssetSourceHelpers.GetFileName(relativePath))
    {
    }

    /// <summary>Wraps an advisory already read.</summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="fileName">The name of its forecast zip or folder, the dataset's id.</param>
    public TropicalCycloneDatasetProcessor(TropicalCycloneAdvisory advisory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Advisory = advisory;
        FileName = fileName;
        _metadata = BuildMetadata(advisory);
    }

    /// <summary>The spec every advisory reports: <c>TC/1.0.0</c>, this model's version.</summary>
    public static SpecRef TropicalCycloneSpec { get; } = SpecRef.Parse("TC/1.0.0");

    /// <inheritdoc />
    public SpecRef Spec => TropicalCycloneSpec;

    /// <summary>The name of the dataset's forecast zip or folder.</summary>
    public string FileName { get; }

    /// <summary>The advisory.</summary>
    public TropicalCycloneAdvisory Advisory { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The advisory's WGS-84 extent (cone, tracks and forecast radii), and the
    /// time from the first past-track point to the last forecast point.
    /// </remarks>
    public DatasetMetadata Metadata => _metadata;

    /// <summary>Reads the metadata of the advisory at <paramref name="path"/>: its spec, extent and time coverage.</summary>
    /// <param name="path">The forecast zip or folder.</param>
    public static DatasetMetadata ReadMetadata(string path) => BuildMetadata(ReadNamed(path));

    /// <summary>Builds the dataset metadata of an advisory.</summary>
    /// <param name="advisory">The advisory.</param>
    public static DatasetMetadata BuildMetadata(TropicalCycloneAdvisory advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        return new DatasetMetadata
        {
            Spec = TropicalCycloneSpec,
            Extent = advisory.Extent,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = new TimeCoverage(advisory.TrackStart, advisory.ForecastEnd),
        };
    }

    /// <inheritdoc />
    /// <remarks>Picking comes with the portrayal (issue #1296); until then nothing is found.</remarks>
    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() => new TropicalCycloneDatasetData(Advisory, _metadata);

    /// <summary>Reads the advisory zips from a stream, the catalog route: the forecast zip only.</summary>
    internal static TropicalCycloneDatasetData OpenData(Stream stream)
    {
        // A file stream's siblings are on disk beside it.
        var advisory = stream is FileStream { Name: { } path } && NhcAdvisoryFiles.Find(path) is { } files
            ? NhcAdvisoryReader.Read(files)
            : NhcAdvisoryReader.Read(stream, null, null);
        return new TropicalCycloneDatasetData(advisory, BuildMetadata(advisory));
    }

    private static TropicalCycloneAdvisory ReadNamed(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return NhcAdvisoryReader.Read(path);
        }
        catch (NhcFormatException ex) when (!ex.Message.StartsWith(Path.GetFileName(path), StringComparison.Ordinal))
        {
            throw new NhcFormatException($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}: {ex.Message}", ex);
        }
    }

    private static TropicalCycloneAdvisory ReadFromSource(IAssetSource source, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        var fileName = AssetSourceHelpers.GetFileName(relativePath);
        using var forecast = AssetSourceHelpers.OpenSeekable(source, relativePath);
        Stream? radii = null;
        Stream? bestTrack = null;
        try
        {
            if (NhcAdvisoryFiles.TryParseFileName(fileName, out var kind, out var atcfId, out var advisory) && kind == NhcFileKind.Forecast)
            {
                var folder = relativePath[..^fileName.Length];
                radii = TryOpen(source, folder + NhcAdvisoryFiles.WindRadiiZipName(atcfId, advisory));
                bestTrack = TryOpen(source, folder + NhcAdvisoryFiles.BestTrackZipName(atcfId));
            }

            return NhcAdvisoryReader.Read(forecast, radii, bestTrack);
        }
        catch (NhcFormatException ex)
        {
            throw new NhcFormatException($"{fileName}: {ex.Message}", ex);
        }
        finally
        {
            radii?.Dispose();
            bestTrack?.Dispose();
        }
    }

    // A sibling zip the source doesn't have is simply not read.
    private static Stream? TryOpen(IAssetSource source, string relativePath)
    {
        try
        {
            return AssetSourceHelpers.OpenSeekable(source, relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
