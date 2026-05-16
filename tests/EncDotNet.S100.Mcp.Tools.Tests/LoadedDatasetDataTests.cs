using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Compile-time + shape assertions for the <see cref="LoadedDatasetData"/>
/// discriminated union, ensuring every variant introduced by PR MCP-3
/// constructs cleanly and exposes its underlying typed handle.
/// </summary>
public class LoadedDatasetDataTests
{
    [Fact]
    public void S101_variant_carries_dataset_handle()
    {
        var dataset = S101Synth.Dataset();
        LoadedDatasetData data = new S101DatasetData(dataset);

        var typed = Assert.IsType<S101DatasetData>(data);
        Assert.Same(dataset, typed.Dataset);
    }

    [Fact]
    public void S131_variant_carries_model_handle()
    {
        var model = S131Synth.Dataset();
        LoadedDatasetData data = new S131DatasetData(model);

        var typed = Assert.IsType<S131DatasetData>(data);
        Assert.Same(model, typed.Model);
    }
}
