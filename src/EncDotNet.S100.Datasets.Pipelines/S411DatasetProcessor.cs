using System;
using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S411DatasetProcessor : GmlDatasetProcessorBase<S411Feature>
{
    private readonly S411Dataset _dataset;

    public override string ProductSpec => "S-411";
    protected override string ProductDescription => "Sea Ice";
    protected override IReadOnlyList<S411Feature> Features => _dataset.Features;

    /// <summary>
    /// Time samples this dataset can be rendered at. S-411 datasets are
    /// snapshot-per-file; this is either a single-element list with the
    /// dataset's <see cref="S411Dataset.IssueDate"/> or empty when the
    /// source GML carried no recognised timestamp.
    /// </summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.IssueDate is { } dt ? [dt] : Array.Empty<DateTime>();

    public S411DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S411DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S411DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            featureCatalogueManager)
    {
    }

    private S411DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S411PortrayalCatalogue(catalogueManager.GetProvider("S-411")),
            featureCatalogueManager?.GetDecoder("S-411"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S411Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S411FeatureXmlSource(_dataset);

    protected override DatasetResult? CheckPreRender(RenderContext? context)
    {
        if (context is S411RenderContext { TimeStep: { } t }
            && _dataset.IssueDate is { } issued
            && t < issued)
        {
            return new DatasetResult
            {
                Layers = Array.Empty<ILayer>(),
                Extent = ComputeExtent(),
                Info = $"S-411 Sea Ice — {FileName}\nHidden (snapshot at {issued:u} is after slider time {t:u})",
                ProductSpec = "S-411",
            };
        }
        return null;
    }
}
