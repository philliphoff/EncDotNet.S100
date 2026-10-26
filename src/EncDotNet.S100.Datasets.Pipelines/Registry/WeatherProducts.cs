using EncDotNet.S100.Datasets.NetCdf;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The built-in <see cref="ProductRegistration"/>s for weather model data,
/// formats that aren't S-100 products (design note weather-sources.md §3.1
/// and §3.2): NetCDF-CF today, GRIB2 later. They're kept apart from
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
    public static IReadOnlyList<ProductRegistration> All { get; } = Array.AsReadOnly(new[] { NetCdf });

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
