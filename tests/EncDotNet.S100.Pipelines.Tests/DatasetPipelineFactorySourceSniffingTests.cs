using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that the source-based
/// <see cref="DatasetPipelineFactory.CreateProcessor(IAssetSource, string, string?, System.Collections.Generic.IReadOnlyDictionary{string, string}?)"/>
/// overload content-sniffs a GML dataset when the exchange-set catalogue
/// omits a machine-readable product identifier. Real-world JCOMM S-411
/// exchange sets (e.g. Canadian Ice Service Hudson Bay sets) declare only a
/// human-readable product-specification name — "Ice Information Product
/// Specification (JCOMM S-411)" — with no <c>productIdentifier</c> or
/// <c>number</c>, so the dataset must be recognized from its GML root element.
/// </summary>
public class DatasetPipelineFactorySourceSniffingTests
{
    private const string JcommIceGml =
        "<?xml version='1.0' encoding='utf-8'?>\n" +
        "<ice:IceDataSet xmlns:gml=\"http://www.opengis.net/gml/3.2\" " +
        "xmlns:ice=\"http://www.jcomm.info/ice\">" +
        "<ice:IceFeatureMember><ice:seaice gml:id=\"seaice.None\">" +
        "<ice:iceact>50</ice:iceact>" +
        "<gml:Polygon srsName=\"http://www.opengis.net/def/crs/EPSG/0/4326\" gml:id=\"seaice.Noneg\">" +
        "<gml:exterior><gml:LinearRing><gml:posList>61.60 -66.84 61.55 -66.92 61.46 -66.96 61.60 -66.84</gml:posList>" +
        "</gml:LinearRing></gml:exterior></gml:Polygon>" +
        "</ice:seaice></ice:IceFeatureMember></ice:IceDataSet>";

    private static DatasetPipelineFactory CreateFactory()
    {
        var pcManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                pcManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            pcManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
    }

    [Fact]
    public void CreateProcessor_SniffsJcommIceGml_WhenCatalogueDeclaresNoProductIdentifier()
    {
        var dir = Path.Combine(Path.GetTempPath(), "s411-sniff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ice.gml"), JcommIceGml);
            using var source = FileSystemAssetSource.Create(dir);
            var factory = CreateFactory();

            // declaredProductSpec is null, mirroring a JCOMM S-411 catalogue
            // that omits productIdentifier/number.
            var processor = factory.CreateProcessor(source, "ice.gml", declaredProductSpec: null);

            Assert.IsType<S411DatasetProcessor>(processor);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreateProcessor_PrefersDeclaredSpec_OverContentSniffing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "s411-sniff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ice.gml"), JcommIceGml);
            using var source = FileSystemAssetSource.Create(dir);
            var factory = CreateFactory();

            // A declared, recognized spec short-circuits content sniffing.
            var processor = factory.CreateProcessor(source, "ice.gml", declaredProductSpec: "S-411");

            Assert.IsType<S411DatasetProcessor>(processor);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
