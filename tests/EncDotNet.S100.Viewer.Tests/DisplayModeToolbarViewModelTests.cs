using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class DisplayModeToolbarViewModelTests
{
    private static DatasetEntry MakeEntry(string spec) =>
        new($"/tmp/{spec}.gml", spec);

    private static FakeDatasetLoaderService LoaderWith(params IDatasetProcessor[] processors)
    {
        var map = new Dictionary<DatasetEntry, IDatasetProcessor>();
        foreach (var p in processors)
            map[MakeEntry(p.Spec.Name)] = p;
        return new FakeDatasetLoaderService { Processors = map };
    }

    [Fact]
    public void Options_PopulateAndOrder_ForMultiModeSpec()
    {
        var state = new EcdisDisplayState();
        // Deliberately unordered declared set.
        var processor = new FakeDisplayModeProcessor("S-411",
            S411DisplayModes.NavigationalModeId,
            S411DisplayModes.ConcentrationModeId,
            S411DisplayModes.StageOfDevelopmentModeId);
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsEnabled);
        Assert.Equal(
            new[]
            {
                S411DisplayModes.ConcentrationModeId,
                S411DisplayModes.StageOfDevelopmentModeId,
                S411DisplayModes.NavigationalModeId,
            },
            vm.Options.Select(o => o.Id).ToArray());

        // Default selection is the concentration mode when none is set.
        Assert.Equal(S411DisplayModes.ConcentrationModeId,
            vm.Options.Single(o => o.IsSelected).Id);
    }

    [Fact]
    public void ProvisionalFlag_SetForNavigationalOnly()
    {
        var state = new EcdisDisplayState();
        var processor = new FakeDisplayModeProcessor("S-411",
            S411DisplayModes.ConcentrationModeId,
            S411DisplayModes.StageOfDevelopmentModeId,
            S411DisplayModes.NavigationalModeId);
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        Assert.True(vm.Options.Single(o => o.Id == S411DisplayModes.NavigationalModeId).IsProvisional);
        Assert.False(vm.Options.Single(o => o.Id == S411DisplayModes.ConcentrationModeId).IsProvisional);
        Assert.False(vm.Options.Single(o => o.Id == S411DisplayModes.StageOfDevelopmentModeId).IsProvisional);
    }

    [Fact]
    public void Selector_Hidden_ForSingleModeSpec()
    {
        var state = new EcdisDisplayState();
        var processor = new FakeDisplayModeProcessor("S-411", S411DisplayModes.ConcentrationModeId);
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        Assert.False(vm.IsVisible);
        Assert.False(vm.IsEnabled);
        Assert.Empty(vm.Options);
    }

    [Fact]
    public void Selector_Hidden_WhenNoDisplayModeAwareProcessor()
    {
        var state = new EcdisDisplayState();
        var processor = new PlainProcessor("S-101");
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        Assert.False(vm.IsVisible);
        Assert.Empty(vm.Options);
    }

    [Fact]
    public void SelectingMode_UpdatesStateAndSnapshot()
    {
        var state = new EcdisDisplayState();
        var processor = new FakeDisplayModeProcessor("S-411",
            S411DisplayModes.ConcentrationModeId,
            S411DisplayModes.StageOfDevelopmentModeId,
            S411DisplayModes.NavigationalModeId);
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        var sod = vm.Options.Single(o => o.Id == S411DisplayModes.StageOfDevelopmentModeId);
        sod.SelectCommand.Execute(null);

        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, state.GetDisplayMode("S-411"));
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId,
            state.Snapshot().ActiveDisplayModes["S-411"]);

        // Selection reflected in the options.
        Assert.True(sod.IsSelected);
        Assert.False(vm.Options.Single(o => o.Id == S411DisplayModes.ConcentrationModeId).IsSelected);
    }

    [Fact]
    public void ExternalStateChange_UpdatesSelection()
    {
        var state = new EcdisDisplayState();
        var processor = new FakeDisplayModeProcessor("S-411",
            S411DisplayModes.ConcentrationModeId,
            S411DisplayModes.StageOfDevelopmentModeId,
            S411DisplayModes.NavigationalModeId);
        using var vm = new DisplayModeToolbarViewModel(state, LoaderWith(processor));

        state.SetDisplayMode("S-411", S411DisplayModes.NavigationalModeId);

        Assert.True(vm.Options.Single(o => o.Id == S411DisplayModes.NavigationalModeId).IsSelected);
        Assert.False(vm.Options.Single(o => o.Id == S411DisplayModes.ConcentrationModeId).IsSelected);
    }

    private sealed class FakeDisplayModeProcessor : IDatasetProcessor, IDisplayModeAwareDatasetProcessor
    {
        public FakeDisplayModeProcessor(string spec, params string[] modeIds)
        {
            Spec = new SpecRef(spec, default);
            DeclaredDisplayModeIds = modeIds;
        }

        public SpecRef Spec { get; }
        public IReadOnlyCollection<string> DeclaredDisplayModeIds { get; }
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }

    private sealed class PlainProcessor : IDatasetProcessor
    {
        public PlainProcessor(string spec) => Spec = new SpecRef(spec, default);
        public SpecRef Spec { get; }
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
