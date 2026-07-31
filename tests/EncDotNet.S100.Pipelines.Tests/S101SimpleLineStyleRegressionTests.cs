using System.Runtime.ExceptionServices;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
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

        var missingAssets = new List<string>();
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            // Scope detection to the #286 symptom specifically: the synthetic
            // "_simple_" line-style sentinel being resolved against the
            // catalogue. Matching only that sentinel (rather than any missing
            // asset) keeps this process-global handler from observing
            // deliberate missing-asset exceptions thrown by other tests running
            // in parallel. The sentinel miss now surfaces as
            // PortrayalAssetNotFoundException; KeyNotFoundException is still
            // checked for any legacy lookup path.
            var isSentinelMiss =
                (e.Exception is PortrayalAssetNotFoundException pex &&
                 string.Equals(pex.AssetName, LineInstruction.SimpleLineStyleReference, StringComparison.OrdinalIgnoreCase))
                || (e.Exception is KeyNotFoundException &&
                    e.Exception.Message.Contains(LineInstruction.SimpleLineStyleReference, StringComparison.OrdinalIgnoreCase));

            if (isSentinelMiss)
            {
                lock (missingAssets)
                    missingAssets.Add(e.Exception.Message);
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
            missingAssets.Count == 0,
            $"Building the S-101 vector portrayal raised {missingAssets.Count} " +
            $"missing-asset exception(s) for the '_simple_' sentinel: {string.Join("; ", missingAssets)}");
    }
}
