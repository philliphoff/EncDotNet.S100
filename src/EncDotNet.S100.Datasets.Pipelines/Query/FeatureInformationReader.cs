using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// A feature's Object Information, decoded with its product's feature
/// catalogue, and the feature it was read from (for its geometry).
/// </summary>
/// <param name="Info">
/// The feature as the viewer's Object Information shows it: the catalogue
/// type name and the attribute rows, named and with listed values decoded.
/// </param>
/// <param name="Feature">The feature, for its geometry; <see langword="null"/> when it couldn't be found again.</param>
/// <param name="UseType">
/// The feature type's use in its catalogue (<c>featureUseType</c>:
/// <c>geographic</c>, <c>meta</c>, <c>cartographic</c>, <c>theme</c>);
/// <see langword="null"/> when the catalogue doesn't say.
/// </param>
public sealed record FeatureInformation(FeatureInfo Info, IS100Feature? Feature, string? UseType = null);

/// <summary>
/// Reads the Object Information of a feature in a headless catalogue's
/// dataset (<see cref="IDatasetCatalog"/>) the way the viewer's processors
/// build it (<see cref="IDatasetProcessor.GetFeatureInfo"/>): S-101, S-401
/// and translated S-57 cells from the feature record, with repeated and
/// complex attributes and the information types the feature links to; AML
/// from its cell; GML products from their simple and complex attributes.
/// Names, listed values and units come from the product's feature catalogue.
/// </summary>
/// <remarks>Thread-safe: catalogues are parsed once and cached.</remarks>
public sealed class FeatureInformationReader : IDisposable
{
    private readonly FeatureCatalogueManager _catalogues;

    /// <summary>Creates a reader over the feature catalogues bundled in <c>EncDotNet.S100.Specifications</c>.</summary>
    public FeatureInformationReader()
        : this(Specification.TryOpenFeatureCatalogue)
    {
    }

    /// <summary>Creates a reader over the feature catalogues <paramref name="catalogueResolver"/> opens.</summary>
    /// <param name="catalogueResolver">Opens a product's feature catalogue XML ("S-101"), or returns <see langword="null"/>.</param>
    public FeatureInformationReader(Func<string, Stream?> catalogueResolver)
    {
        ArgumentNullException.ThrowIfNull(catalogueResolver);
        _catalogues = new FeatureCatalogueManager(catalogueResolver);
    }

    /// <summary>
    /// The Object Information of the feature <paramref name="featureId"/> in
    /// <paramref name="dataset"/>, or <see langword="null"/> when the dataset
    /// has no such vector feature.
    /// </summary>
    /// <param name="dataset">The dataset the feature is in.</param>
    /// <param name="featureId">The feature's id, as <see cref="IdentifyMatch.FeatureId"/> reports it.</param>
    /// <param name="bounds">The feature's bounds when known, to find it without reading the whole dataset.</param>
    public FeatureInformation? Read(LoadedDataset dataset, string featureId, BoundingBox? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrEmpty(featureId);

        var feature = Find(dataset, featureId, bounds);
        var decoder = Decoder(dataset.Spec.Name);

        var information = Information(dataset, featureId, feature, decoder);
        return information is null
            ? null
            : information with { UseType = UseType(decoder, information.Info.FeatureType) };
    }

    private static FeatureInformation? Information(LoadedDataset dataset, string featureId, IS100Feature? feature, FeatureCatalogueDecoder? decoder)
    {
        switch (dataset.Data)
        {
            case AmlDatasetData { Cell: { } cell }:
                return cell.Find(featureId) is { } amlFeature
                    ? new FeatureInformation(cell.BuildFeatureInfo(amlFeature, drawingPriority: null), feature)
                    : null;

            case S101DatasetData s101:
                return S101Info(s101, featureId, feature, decoder) is { } info ? new FeatureInformation(info, feature) : null;
        }

        if (feature is null)
            return null;

        return new FeatureInformation(new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = FeatureInfoBuilder.Build(
                feature.Attributes,
                feature.ComplexAttributes.Select(c => new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
                decoder),
            Geometry = feature.GeometryType,
        }, feature);
    }

    // As S101DatasetProcessor.BuildFeatureInfo: from the record, with its
    // information associations and referenced texts.
    private static FeatureInfo? S101Info(S101DatasetData s101, string featureId, IS100Feature? feature, FeatureCatalogueDecoder? decoder)
    {
        if (!uint.TryParse(featureId, NumberStyles.None, CultureInfo.InvariantCulture, out var recordId))
            return null;

        var document = s101.Dataset.Document;
        var record = document.Features.FirstOrDefault(r => r.RecordId == recordId);
        if (record is null)
            return null;

        IReadOnlyList<PickAttribute> attributes =
        [
            .. S101PickReport.Attributes(document, record.Attributes, decoder),
            .. S101PickReport.InformationRows(document, record, decoder),
        ];
        if (s101.ExternalTextResolver is { } resolver)
            attributes = FeatureInfoBuilder.ResolveFileReferences(attributes, resolver);

        var type = feature?.FeatureType
            ?? (document.FeatureTypeCatalogue.TryGetValue(record.FeatureTypeCode, out var acronym)
                ? acronym
                : record.FeatureTypeCode.ToString(CultureInfo.InvariantCulture));
        return new FeatureInfo
        {
            FeatureRef = featureId,
            FeatureType = type,
            FeatureTypeName = TypeName(decoder, type),
            Attributes = attributes,
            Geometry = feature?.GeometryType ?? default,
        };
    }

    // A cell made to S-101 1.x names some types differently ("BuoyLateral" for
    // "LateralBuoy"): read the name the bundled 2.0.0 catalogue gives the type
    // it became, as the portrayal does.
    private static string? TypeName(FeatureCatalogueDecoder? decoder, string type) =>
        decoder?.ResolveFeatureTypeName(type) ?? decoder?.ResolveFeatureTypeName(S101LegacyFeatureNames.Normalize(type));

    private static string? UseType(FeatureCatalogueDecoder? decoder, string type)
    {
        if (decoder is null)
            return null;
        var current = S101LegacyFeatureNames.Normalize(type);
        return decoder.Catalogue.FeatureTypes.FirstOrDefault(t => t.Code == type || t.Code == current)?.FeatureUseType;
    }

    private static IS100Feature? Find(LoadedDataset dataset, string featureId, BoundingBox? bounds)
    {
        foreach (var feature in FeatureAccessor.GetFeatures(dataset, bounds) ?? [])
        {
            if (string.Equals(feature.Id, featureId, StringComparison.Ordinal))
                return feature;
        }

        return null;
    }

    // S-57 cells are read translated to S-101, so they decode with its catalogue.
    private FeatureCatalogueDecoder? Decoder(string specName)
    {
        var spec = specName == "S-57" ? "S-101" : specName;
        try
        {
            return _catalogues.GetDecoder(spec);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Releases the parsed catalogues.</summary>
    public void Dispose() => _catalogues.Dispose();
}
