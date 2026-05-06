using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class PickReportViewModelTests
{
    private static PickAttribute Leaf(string code, string value, string? name = null) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = null, Children = [] };

    [Fact]
    public void SetPick_PopulatesAllFieldsAndMarksHasPick()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "DepthArea",
            featureTypeName: "Depth Area",
            featureRef: "42",
            datasetFileName: "test.000",
            productSpec: "S-101",
            attributes: new[]
            {
                Leaf("DRVAL1", "10.0", "Depth Range Value 1"),
                Leaf("DRVAL2", "20.0", "Depth Range Value 2"),
            });

        Assert.True(vm.HasPick);
        Assert.Equal("DepthArea", vm.FeatureType);
        Assert.Equal("Depth Area", vm.FeatureTypeName);
        Assert.Equal("42", vm.FeatureRef);
        Assert.Equal("test.000", vm.DatasetFileName);
        Assert.Equal("S-101", vm.ProductSpec);
        Assert.Equal(2, vm.Attributes.Count);
        Assert.True(vm.HasAttributes);
        Assert.Equal("DRVAL1", vm.Attributes[0].Code);
        Assert.Equal("Depth Range Value 1", vm.Attributes[0].DisplayName);
        Assert.Equal("10.0", vm.Attributes[0].RawValue);
    }

    [Fact]
    public void SetPick_WithEmptyAttributes_ReportsHasAttributesFalse()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "Authority",
            featureTypeName: null,
            featureRef: "auth.1",
            datasetFileName: "container.gml",
            productSpec: "S-127",
            attributes: System.Array.Empty<PickAttribute>());

        Assert.True(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void SetPick_ReplacesPreviousPickContents()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("First", null, "1", "a.gml", "S-101", new[] { Leaf("X", "1") });
        vm.SetPick("Second", null, "2", "b.gml", "S-124", new[] { Leaf("Y", "2") });

        Assert.Equal("Second", vm.FeatureType);
        Assert.Equal("2", vm.FeatureRef);
        Assert.Equal("b.gml", vm.DatasetFileName);
        Assert.Equal("S-124", vm.ProductSpec);
        Assert.Single(vm.Attributes);
        Assert.Equal("Y", vm.Attributes[0].Code);
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("DepthArea", "Depth Area", "42", "test.000", "S-101",
            new[] { Leaf("DRVAL1", "10.0") });

        vm.Clear();

        Assert.False(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Null(vm.FeatureType);
        Assert.Null(vm.FeatureTypeName);
        Assert.Null(vm.FeatureRef);
        Assert.Null(vm.DatasetFileName);
        Assert.Null(vm.ProductSpec);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void ClearCommand_InvokesClear()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("X", null, "1", null, null, System.Array.Empty<PickAttribute>());

        Assert.True(vm.HasPick);
        vm.ClearCommand.Execute(null);

        Assert.False(vm.HasPick);
    }

    [Fact]
    public void HasPickPropertyChanged_FiresOnSetPickAndClear()
    {
        var vm = new PickReportViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PickReportViewModel.HasPick))
                changed.Add(e.PropertyName);
        };

        vm.SetPick("X", null, "1", null, null, System.Array.Empty<PickAttribute>());
        vm.Clear();

        Assert.Equal(2, changed.Count);
    }
}
