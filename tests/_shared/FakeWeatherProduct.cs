using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// A fake non-S-100 product registered under <c>"GRIB2"</c> (issue #1061): it
/// claims files by their leading bytes alone (<see cref="ProductRegistration.MatchBytes"/>,
/// a GRIB edition 2 indicator section), so a file with no useful extension
/// loads; its processor draws one solid area; and its traits say it isn't S-100
/// and is drawn by an application portrayal. Tests register it and check the
/// hosts (detection, construction, the catalog, <c>list_specs</c>, the viewer
/// badge and pick notice) handle it without any host change.
/// </summary>
internal static class FakeWeatherProduct
{
    /// <summary>The spec the fake registers under.</summary>
    public const string Spec = "GRIB2";

    /// <summary>The fill colour the processor paints its one area with.</summary>
    public const string FillHex = "#00FF00";

    /// <summary>The product's own pick-report line.</summary>
    public const string Notice = "Fake weather model, not for navigation.";

    /// <summary>The area the processor draws and the metadata reports.</summary>
    public static BoundingBox Bounds { get; } = new(-1, -1, 1, 1);

    /// <summary>The fake's declared spec, <c>GRIB2/2.0.0</c>.</summary>
    public static SpecRef SpecRef { get; } = SpecRef.Parse("GRIB2/2.0.0");

    /// <summary>The fake's registration.</summary>
    /// <param name="notice">Optional <see cref="ProductRegistration.PickReportNotice"/>.</param>
    public static ProductRegistration Registration(string? notice = null) => new()
    {
        Spec = Spec,
        CreateFromPath = (_, path) => new Processor(Path.GetFileName(path)),
        CreateFromSource = (_, request) => new Processor(Path.GetFileName(request.RelativePath)),
        IsS100 = false,
        Portrayal = PortrayalBasis.Application("GRIB2 Ed 2"),
        PickReportNotice = notice,
        // WMO FM 92 GRIB edition 2, Section 0: "GRIB", two reserved octets,
        // the discipline, then the edition number in octet 8.
        MatchBytes = static header => header.StartsWithAscii("GRIB")
            && header.LeadingBytes.Length >= 8
            && header.LeadingBytes.Span[7] == 2,
        ReadMetadata = static _ => Metadata,
        OpenData = static (_, _) => new Data(),
        DefaultPlanes = new Dictionary<string, S98DisplayPlane>
        {
            [ProductRegistration.AnyLayerKind] = S98DisplayPlane.OnDemandSurface,
        },
    };

    /// <summary>The metadata the fake's reader and payload report.</summary>
    public static DatasetMetadata Metadata { get; } = new() { Spec = SpecRef, Extent = Bounds };

    /// <summary>
    /// Writes a minimal GRIB2-looking file (Section 0 then padding) named
    /// <paramref name="fileName"/> — with no extension by default, like a NOMADS
    /// GRIB filter download — and returns its path.
    /// </summary>
    public static string Write(string directory, string fileName = "gfs.t06z.pgrb2.0p25.f000-filter")
    {
        var bytes = new byte[64];
        "GRIB"u8.CopyTo(bytes);
        bytes[6] = 0; // discipline: meteorological
        bytes[7] = 2; // edition
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The fake's catalog payload.</summary>
    public sealed record Data : ProductDatasetData
    {
        /// <inheritdoc />
        public override DatasetMetadata Metadata => FakeWeatherProduct.Metadata;
    }

    /// <summary>The fake's processor: one solid area over <see cref="Bounds"/>.</summary>
    public sealed class Processor(string fileName) : IDatasetProcessor, IVectorPortrayalSource
    {
        private const string FeatureRef = "1";
        private const string Token = "WIND";

        /// <inheritdoc />
        public SpecRef Spec => SpecRef;

        /// <inheritdoc />
        public DatasetMetadata Metadata => FakeWeatherProduct.Metadata;

        /// <inheritdoc />
        public FeatureInfo? GetFeatureInfo(string featureRef) => featureRef == FeatureRef
            ? new FeatureInfo { FeatureRef = FeatureRef, FeatureType = "WindSpeed", Attributes = [] }
            : null;

        /// <inheritdoc />
        public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var area = new AreaInstruction { FeatureReference = FeatureRef, FillColor = Token };
            var result = new VectorPortrayalResult
            {
                SubLayers =
                [
                    new VectorSubLayer
                    {
                        LayerKey = "grib2.wind",
                        LayerName = fileName,
                        Instructions = [area],
                        Plane = S98DisplayPlane.OnDemandSurface,
                        SourceFeatureType = "area",
                    },
                ],
                Palette = new ColorPalette("fake", new Dictionary<string, string> { [Token] = FillHex }),
                GeometryProvider = new SurfaceProvider(),
                Product = FakeWeatherProduct.Spec,
                Spec = SpecRef,
                SourceDatasetId = fileName,
                Info = "Fake GRIB2",
            };
            return Task.FromResult(result);
        }

        private sealed class SurfaceProvider : IFeatureGeometryProvider
        {
            private static readonly FeatureGeometry Surface = new()
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(Bounds.SouthLatitude, Bounds.WestLongitude),
                    new GeoPosition(Bounds.SouthLatitude, Bounds.EastLongitude),
                    new GeoPosition(Bounds.NorthLatitude, Bounds.EastLongitude),
                    new GeoPosition(Bounds.NorthLatitude, Bounds.WestLongitude),
                    new GeoPosition(Bounds.SouthLatitude, Bounds.WestLongitude),
                ],
            };

            public FeatureGeometry? GetGeometry(string featureReference) =>
                featureReference == FeatureRef ? Surface : null;
        }
    }
}
