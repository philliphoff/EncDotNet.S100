using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using System.Collections.Immutable;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class LoadedDatasetFactory
{
    public static SpecRef S101Spec => new("S-101", new SpecVersion(1, 0, 0));
    public static SpecRef S124Spec => new("S-124", new SpecVersion(1, 1, 0));
    public static SpecRef S122Spec => new("S-122", new SpecVersion(1, 0, 0));
    public static SpecRef S131Spec => new("S-131", new SpecVersion(1, 0, 0));
    public static SpecRef S102Spec => new("S-102", new SpecVersion(2, 1, 0));
    public static SpecRef S104Spec => new("S-104", new SpecVersion(1, 0, 0));

    public static BoundingBox Box(double s = -1, double w = -1, double n = 1, double e = 1) =>
        new(s, w, n, e);

    public static LoadedDataset S101(
        string id,
        S101Dataset? dataset = null,
        BoundingBox? bounds = null)
    {
        return new LoadedDataset(
            new DatasetId(id),
            S101Spec,
            bounds ?? Box(),
            null,
            new S101DatasetData(dataset ?? S101Synth.Dataset()));
    }

    public static LoadedDataset S124(
        string id,
        S124Dataset? model = null,
        BoundingBox? bounds = null)
    {
        return new LoadedDataset(
            new DatasetId(id),
            S124Spec,
            bounds ?? Box(),
            null,
            new S124DatasetData(model ?? S124Synth.Dataset()));
    }

    public static LoadedDataset S122(string id, BoundingBox? bounds = null)
    {
        var model = new S122Dataset
        {
            Features = ImmutableArray<S122Feature>.Empty,
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        return new LoadedDataset(
            new DatasetId(id),
            S122Spec,
            bounds ?? Box(),
            null,
            new S122DatasetData(model));
    }

    public static LoadedDataset S131(
        string id,
        S131Dataset? model = null,
        BoundingBox? bounds = null)
    {
        return new LoadedDataset(
            new DatasetId(id),
            S131Spec,
            bounds ?? Box(),
            null,
            new S131DatasetData(model ?? S131Synth.Dataset()));
    }

    public static LoadedDataset S102(
        string id,
        BoundingBox? bounds = null,
        S102CoverageSource? source = null)
    {
        return new LoadedDataset(
            new DatasetId(id),
            S102Spec,
            bounds ?? Box(0, 0, 0.04, 0.04),
            null,
            new S102CoverageData(source ?? S102Synth.Source()));
    }
}
