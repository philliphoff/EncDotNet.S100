using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression coverage for an S-100 Edition 5.2 exchange set laid out the way
/// PRIMAR distributes it: an <c>xc/5.2</c> catalogue, <c>file:/</c>-rooted
/// file names nested as <c>&lt;product&gt;/DATASET_FILES/&lt;agency&gt;/&lt;name&gt;/&lt;edition&gt;/</c>,
/// a <c>CATALOG.SIGN</c> beside it, and <c>S100_SE_SignatureOnData</c>
/// signatures that omit <c>dataStatus</c>. Such a catalogue used to fail to
/// parse, so the viewer reported that the set contained nothing portrayable.
/// The set is built from IHO test data and synthetic metadata.
/// </summary>
public sealed class S100Edition52ExchangeSetTests : IDisposable
{
    private const string S101Relative = "S-101/DATASET_FILES/AA00/101AA00DS0020/1/101AA00DS0020.000";
    private const string S102Relative = "S-102/DATASET_FILES/US00/102US004MI1CI262227/3/102US004MI1CI262227.h5";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"s100-ed52-{Guid.NewGuid():N}");

    public S100Edition52ExchangeSetTests()
    {
        var datasets = DatasetsRoot();
        Copy(Path.Combine(datasets, "S101", "S-101", "DATASET_FILES", "101AA00DS0020.000"), S101Relative);
        Copy(Path.Combine(datasets, "S102", "102US004MI1CI262227.h5"), S102Relative);
        File.WriteAllText(Path.Combine(_root, "CATALOG.XML"), Catalogue);
        File.WriteAllText(Path.Combine(_root, "CATALOG.SIGN"), "synthetic");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task OpenAsync_ReadsSecurityAndResolvesNestedFileUris()
    {
        using var exchangeSet = await ExchangeSet.OpenAsync(FileSystemAssetSource.Create(_root));

        var datasets = exchangeSet.Catalogue.DatasetDiscoveryMetadata;
        Assert.Equal([S101Relative, S102Relative], datasets.Select(d => d.RelativePath));
        foreach (var dataset in datasets)
        {
            await using var stream = await exchangeSet.FetchDatasetAsync(dataset);
            Assert.True(stream.Length > 0, dataset.RelativePath);

            var signature = Assert.Single(dataset.DigitalSignatures);
            Assert.Equal(DigitalSignatureKind.SignatureOnData, signature.Kind);
            Assert.Equal(SignatureDataStatus.Unencrypted, signature.DataStatus);
        }
    }

    [Fact]
    public async Task LoadAllAsync_PortraysEveryCataloguedDataset()
    {
        using var exchangeSet = await ExchangeSet.OpenAsync(FileSystemAssetSource.Create(_root));
        var loader = new ExchangeSetLoader(CreateFactory());

        var results = new List<ExchangeSetLoadResult>();
        await foreach (var result in loader.LoadAllAsync(exchangeSet))
        {
            results.Add(result);
        }

        try
        {
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Null(r.Error));
            Assert.IsType<S101DatasetProcessor>(results[0].Processor);
            Assert.NotNull(results[1].Processor);
        }
        finally
        {
            foreach (var result in results)
            {
                (result.Processor as IDisposable)?.Dispose();
            }
        }
    }

    private void Copy(string source, string relative)
    {
        var target = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }

    private static string DatasetsRoot([CallerFilePath] string callerFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets");

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

    private static string Discovery(string relative, string name, string identifier) => $"""
                <S100XC:S100_DatasetDiscoveryMetadata>
                    <S100XC:fileName>file:/{relative}</S100XC:fileName>
                    <S100XC:compressionFlag>false</S100XC:compressionFlag>
                    <S100XC:dataProtection>false</S100XC:dataProtection>
                    <S100XC:digitalSignatureReference>ECDSA-384-SHA2</S100XC:digitalSignatureReference>
                    <S100XC:digitalSignatureValue>
                        <S100SE:S100_SE_SignatureOnData id="{Guid.NewGuid()}" certificateRef="urn:mrn:iho:00XX:00001">AQID</S100SE:S100_SE_SignatureOnData>
                    </S100XC:digitalSignatureValue>
                    <S100XC:purpose>newEdition</S100XC:purpose>
                    <S100XC:editionNumber>1</S100XC:editionNumber>
                    <S100XC:updateNumber>0</S100XC:updateNumber>
                    <S100XC:productSpecification>
                        <S100XC:name>{name}</S100XC:name>
                        <S100XC:productIdentifier>{identifier}</S100XC:productIdentifier>
                    </S100XC:productSpecification>
                    <S100XC:encodingFormat>{(identifier == "S-101" ? "ISO/IEC 8211" : "HDF5")}</S100XC:encodingFormat>
                </S100XC:S100_DatasetDiscoveryMetadata>
        """;

    private static readonly string Catalogue = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.2"
            xmlns:S100SE="http://www.iho.int/s100/se/5.2"
            xmlns:gco="http://standards.iso.org/iso/19115/-3/gco/1.0"
            xmlns:lan="http://standards.iso.org/iso/19115/-3/lan/1.0"
            xmlns:cit="http://standards.iso.org/iso/19115/-3/cit/2.0">
            <S100XC:identifier>
                <S100XC:identifier>SYNTHETIC ED 5.2 EXCHANGE SET</S100XC:identifier>
                <S100XC:dateTime>2026-01-01T00:00:00.000000000</S100XC:dateTime>
            </S100XC:identifier>
            <S100XC:certificates>
                <S100SE:schemeAdministrator id="IHO"/>
                <S100SE:certificate id="urn:mrn:iho:00XX:00001" issuer="IHO">AQ==</S100SE:certificate>
            </S100XC:certificates>
            <S100XC:datasetDiscoveryMetadata>
        {Discovery(S101Relative, "Electronic Navigational Chart", "S-101")}
        {Discovery(S102Relative, "Bathymetric Surface", "S-102")}
            </S100XC:datasetDiscoveryMetadata>
            <S100XC:supportFileDiscoveryMetadata/>
        </S100XC:S100_ExchangeCatalogue>
        """;
}
