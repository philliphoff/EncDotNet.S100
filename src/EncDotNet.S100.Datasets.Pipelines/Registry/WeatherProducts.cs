using EncDotNet.S100.Datasets.Grib;
using EncDotNet.S100.Datasets.NetCdf;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The built-in <see cref="ProductRegistration"/>s for weather model data,
/// formats that aren't S-100 products (design note weather-sources.md §3.1
/// and §3.2): GRIB2 and NetCDF-CF. They're kept apart from
/// <see cref="S100Products"/> so a host can register the S-100 products
/// without them (<see cref="ProductRegistryExtensions.AddAllS100Products"/>);
/// <see cref="S100Products.CreateDefaultRegistry"/>, and so
/// <see cref="ProductRegistry.Default"/>, includes both.
/// </summary>
public static class WeatherProducts
{
    // Design note §5: fills on the on-demand surface plane (under S-104),
    // barbs and arrows on the dynamic arrows plane (above S-111). The
    // sub-layers carry their own planes; these are the fallback.
    private static readonly IReadOnlyDictionary<string, S98DisplayPlane> WeatherPlanes =
        new Dictionary<string, S98DisplayPlane>(StringComparer.OrdinalIgnoreCase)
        {
            [Weather.WaveHeightStyle.Key] = S98DisplayPlane.OnDemandSurface,
            [Weather.WindSpeedStyle.Key] = S98DisplayPlane.OnDemandSurface,
            [Weather.WindBarbStyle.Key] = S98DisplayPlane.DynamicArrows,
            [Weather.WaveDirectionStyle.Key] = S98DisplayPlane.DynamicArrows,
            [ProductRegistration.AnyLayerKind] = S98DisplayPlane.OnDemandSurface,
        };

    /// <summary>
    /// The portrayal of weather grids: the code-defined styles of
    /// <c>EncDotNet.S100.Datasets.Weather</c>, since NetCDF-CF and GRIB2 define
    /// no symbology.
    /// </summary>
    public static PortrayalBasis NetCdfPortrayal { get; } = PortrayalBasis.Application("NetCDF-CF");

    /// <summary>
    /// The portrayal of GRIB2 grids: the code-defined weather styles, since
    /// GRIB2 defines no symbology.
    /// </summary>
    public static PortrayalBasis GribPortrayal { get; } = PortrayalBasis.Application("GRIB2");

    /// <summary>
    /// GRIB edition 2 wind and wave grids (<c>GRIB2/2.0.0</c>): NOAA GFS and
    /// GFS-Wave, ECMWF open data and others on a regular latitude/longitude
    /// grid, drawn by <see cref="GribDatasetProcessor"/>.
    /// </summary>
    /// <remarks>
    /// <para>A file is claimed (<see cref="ProductRegistration.MatchBytes"/>) when:</para>
    /// <list type="bullet">
    /// <item><description>
    /// it starts with a GRIB edition 2 indicator section (<c>GRIB</c>, two
    /// reserved octets, the discipline, then 2 in octet 8), whatever its
    /// extension: GRIB2 data has no reliable extension. A NOMADS GRIB filter
    /// download is named after the script (<c>filter_gfs_0p25.pl</c>), NCEP's
    /// own files have none (<c>gfs.t00z.pgrb2.0p25.f003</c>), and others use
    /// <c>.grb</c>, <c>.grb2</c>, <c>.grib2</c>, <c>.grib</c> or <c>.bin</c>;
    /// </description></item>
    /// <item><description>
    /// it's named <c>.grb</c>, <c>.grb2</c>, <c>.grib2</c> or <c>.grib</c> and
    /// holds a <c>GRIB</c> indicator in its first kilobyte of any edition, or
    /// after a WMO bulletin header, so a GRIB edition 1 file reaches the reader,
    /// which says it isn't supported.
    /// </description></item>
    /// </list>
    /// <para>
    /// S-100 encodings never start with <c>GRIB</c> (ISO 8211 starts with the
    /// record length's digits, HDF5 with its signature, GML with <c>&lt;</c>),
    /// and the envelope matchers run first, so the GRIB2 matcher can't claim an
    /// S-100 dataset or a NetCDF file.
    /// </para>
    /// </remarks>
    public static ProductRegistration Grib2 { get; } = new()
    {
        Spec = GribDatasetProcessor.SpecName,
        CreateFromPath = static (s, path) => new GribDatasetProcessor(path, s.CrsTransformFactory),
        CreateFromSource = static (s, r) => new GribDatasetProcessor(r.Source, r.RelativePath, s.CrsTransformFactory),
        IsS100 = false,
        Portrayal = GribPortrayal,
        MatchBytes = static header => IsGrib2(header) || (HasGribExtension(header) && ContainsGribIndicator(header)),
        ReadMetadata = GribDatasetProcessor.ReadMetadata,
        OpenData = static (stream, _) => OpenGrib(stream),
        DefaultPlanes = WeatherPlanes,
    };

    /// <summary>
    /// NetCDF-CF wind and wave grids, NetCDF-4 (<c>NetCDF/4.0.0</c>) and
    /// NetCDF-3 (<c>NetCDF/3.0.0</c>), drawn by
    /// <see cref="NetCdfDatasetProcessor"/>.
    /// </summary>
    /// <remarks>
    /// <para>A file is claimed when:</para>
    /// <list type="bullet">
    /// <item><description>
    /// it starts with <c>CDF\x01</c> or <c>CDF\x02</c> (NetCDF-3), whatever its
    /// extension (<see cref="ProductRegistration.MatchBytes"/>);
    /// </description></item>
    /// <item><description>
    /// it's HDF5 with a root <c>Conventions</c> attribute naming CF (for example
    /// <c>"CF-1.6"</c>) and no S-100 <c>productSpecification</c>, whatever its
    /// extension, so a NetCDF-4 file named <c>.h5</c> is still NetCDF
    /// (<see cref="ProductRegistration.MatchHdf5"/>);
    /// </description></item>
    /// <item><description>
    /// it's HDF5 named <c>.nc</c> or <c>.nc4</c> that no HDF5 product claimed,
    /// so a NetCDF-4 file without a <c>Conventions</c> attribute reaches the
    /// reader, which then says what the file lacks.
    /// </description></item>
    /// </list>
    /// <para>
    /// An S-102, S-104 or S-111 file always declares its product in
    /// <c>productSpecification</c>, which the NetCDF matcher refuses, so the
    /// two never claim the same file.
    /// </para>
    /// </remarks>
    public static ProductRegistration NetCdf { get; } = new()
    {
        Spec = NetCdfDatasetProcessor.SpecName,
        CreateFromPath = static (s, path) => new NetCdfDatasetProcessor(path, s.CrsTransformFactory),
        CreateFromSource = static (s, r) => new NetCdfDatasetProcessor(r.Source, r.RelativePath, s.CrsTransformFactory),
        IsS100 = false,
        Portrayal = NetCdfPortrayal,
        MatchBytes = static header =>
            header.StartsWith("CDF\u0001"u8)
            || header.StartsWith("CDF\u0002"u8)
            || (header.IsHdf5 && (header.HasExtension(".nc") || header.HasExtension(".nc4"))),
        MatchHdf5 = static root => !DeclaresS100Product(root.ProductSpecification)
            && root.ReadStringAttribute("Conventions") is { } conventions
            && conventions.Contains("CF-", StringComparison.OrdinalIgnoreCase),
        ReadMetadata = NetCdfDatasetProcessor.ReadMetadata,
        OpenData = static (stream, _) => OpenNetCdf(stream),
        DefaultPlanes = WeatherPlanes,
    };

    /// <summary>Every built-in weather registration.</summary>
    public static IReadOnlyList<ProductRegistration> All { get; } = Array.AsReadOnly(new[] { Grib2, NetCdf });

    /// <summary>Registers every built-in weather product with <paramref name="registry"/>.</summary>
    /// <param name="registry">The registry.</param>
    public static void RegisterAll(ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var registration in All)
            registry.Register(registration);
    }

    // An S-100 productSpecification names "S-" and a number (INT.IHO.S-102.2.2).
    private static bool DeclaresS100Product(string productSpecification) =>
        !string.IsNullOrWhiteSpace(productSpecification)
        && System.Text.RegularExpressions.Regex.IsMatch(productSpecification, @"S-?\d{2,3}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // WMO FM 92 GRIB edition 2, Section 0: "GRIB", two reserved octets, the
    // discipline, then the edition number in octet 8.
    private static bool IsGrib2(DatasetHeader header) =>
        header.StartsWith("GRIB"u8)
        && header.LeadingBytes.Length >= 8
        && header.LeadingBytes.Span[7] == 2;

    private static bool HasGribExtension(DatasetHeader header) =>
        header.HasExtension(".grb") || header.HasExtension(".grb2")
        || header.HasExtension(".grib2") || header.HasExtension(".grib");

    private static bool ContainsGribIndicator(DatasetHeader header) =>
        header.LeadingBytes.Span.IndexOf("GRIB"u8) >= 0;

    // The catalog payload: the stream is the caller's, so the reader keeps an
    // in-memory copy to decode fields from later. A file stream's name helps
    // name the model.
    private static LoadedDatasetData OpenGrib(Stream stream)
    {
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        var dataset = GribWeatherDataset.Read(
            GribFile.Open(copy.GetBuffer().AsMemory(0, (int)copy.Length)),
            (stream as FileStream)?.Name is { } path ? Path.GetFileName(path) : null);
        var metadata = WeatherGridProcessorBase.BuildMetadata(dataset.GridSet, GribDatasetProcessor.Grib2Spec, dataset.Times);
        return new WeatherGridDatasetData(dataset.GridSet, metadata, dataset.ReferenceTime);
    }

    // The catalog payload: the stream is the caller's, so the reader keeps an
    // in-memory copy to decode time steps from later.
    private static LoadedDatasetData OpenNetCdf(Stream stream)
    {
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        var dataset = NetCdfWeatherDataset.Open(copy);
        var metadata = WeatherGridProcessorBase.BuildMetadata(
            dataset.GridSet, NetCdfDatasetProcessor.SpecFor(dataset.Format), dataset.Times);
        return new WeatherGridDatasetData(dataset.GridSet, metadata, dataset.ReferenceTime);
    }
}
