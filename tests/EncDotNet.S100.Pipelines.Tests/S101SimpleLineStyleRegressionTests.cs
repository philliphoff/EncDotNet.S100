using System.Runtime.ExceptionServices;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Real-data regression cover for issue #286: building the vector portrayal of
/// an S-101 cell used to throw three <see cref="KeyNotFoundException"/>s per
/// load because the catalogue pre-warm tried to resolve the synthetic
/// <c>_simple_</c> line-style sentinel (emitted by the Part 9A
/// <c>SimpleLineStyle</c> portrayal model) against the portrayal catalogue,
/// which never contains it. Skipped when no S-101 cell is installed so CI stays
/// green.
/// </summary>
public class S101SimpleLineStyleRegressionTests
{
    private static string? FindCell()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidateDirs = new[]
        {
            Path.Combine(home, "Downloads", "IC-ENC"),
            Path.Combine(home, "Downloads", "Complete S10X datasets", "S-101 Trial Cells"),
        };

        foreach (var dir in candidateDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            var cell = Directory.EnumerateFiles(dir, "101*.000", SearchOption.AllDirectories)
                .OrderBy(static p => p, StringComparer.Ordinal)
                .FirstOrDefault();
            if (cell is not null)
                return cell;
        }

        return null;
    }

    private static DatasetPipelineFactory CreateFactory()
    {
        var catalogueManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                catalogueManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());
    }

    [SkippableFact]
    public async Task BuildVectorPortrayal_DoesNotThrowForSimpleLineStyle()
    {
        var cell = FindCell();
        Skip.If(cell is null, "No S-101 cell present.");

        var keyNotFound = new List<string>();
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception is KeyNotFoundException knf)
            {
                lock (keyNotFound)
                    keyNotFound.Add(knf.Message);
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            var factory = CreateFactory();
            var processor = (S101DatasetProcessor)factory.CreateProcessor(cell!);
            await processor.BuildVectorPortrayalAsync();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        Assert.True(
            keyNotFound.Count == 0,
            $"Building the S-101 vector portrayal raised {keyNotFound.Count} " +
            $"KeyNotFoundException(s): {string.Join("; ", keyNotFound)}");
    }
}
