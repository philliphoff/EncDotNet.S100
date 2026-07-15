using System;
using System.IO;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Coverage for
/// <see cref="DatasetPipelineFactory.CreateProcessorWithFilesystemUpdates(string)"/>,
/// which gives a single dropped base cell (<c>….000</c>) the same
/// sequential-update application as an exchange-set load (issue #449).
/// Because the S-101/S-57 processors parse their base cell eagerly, the
/// construction paths are exercised against a real cell supplied via the
/// <c>ENCDOTNET_S101_BASE_CELL</c> / <c>ENCDOTNET_S57_BASE_CELL</c>
/// environment variables, and skipped otherwise so CI never depends on
/// (or commits) real ENC data.
/// </summary>
public class DatasetPipelineFactoryFilesystemUpdatesTests
{
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
    public void CreateProcessorWithFilesystemUpdates_RejectsNullOrEmpty()
    {
        var factory = CreateFactory();
        Assert.Throws<ArgumentException>(
            () => factory.CreateProcessorWithFilesystemUpdates(""));
    }

    [SkippableFact]
    public void CreateProcessorWithFilesystemUpdates_RealS101BaseCell_BuildsS101Processor()
    {
        var basePath = Environment.GetEnvironmentVariable("ENCDOTNET_S101_BASE_CELL");
        Skip.If(string.IsNullOrEmpty(basePath), "ENCDOTNET_S101_BASE_CELL not set.");
        Skip.IfNot(File.Exists(basePath!), $"Base cell not found: {basePath}.");

        var factory = CreateFactory();

        var processor = factory.CreateProcessorWithFilesystemUpdates(basePath!);

        // Whether or not the cell has sibling updates on disk, an S-101
        // base cell must produce an S-101 processor.
        Assert.IsType<S101DatasetProcessor>(processor);
    }

    [SkippableFact]
    public void CreateProcessorWithFilesystemUpdates_RealS57BaseCell_BuildsS57Processor()
    {
        var basePath = Environment.GetEnvironmentVariable("ENCDOTNET_S57_BASE_CELL");
        Skip.If(string.IsNullOrEmpty(basePath), "ENCDOTNET_S57_BASE_CELL not set.");
        Skip.IfNot(File.Exists(basePath!), $"Base cell not found: {basePath}.");

        var factory = CreateFactory();

        var processor = factory.CreateProcessorWithFilesystemUpdates(basePath!);

        Assert.IsType<S57DatasetProcessor>(processor);
    }
}
