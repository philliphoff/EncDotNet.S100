using System.Collections.Generic;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class PickReportViewModelTests
{
    [Fact]
    public void SetPick_PopulatesAllFieldsAndMarksHasPick()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "DepthArea",
            featureRef: "42",
            datasetFileName: "test.000",
            productSpec: "S-101",
            attributes: new Dictionary<string, string?>
            {
                ["DRVAL1"] = "10.0",
                ["DRVAL2"] = "20.0",
            });

        Assert.True(vm.HasPick);
        Assert.Equal("DepthArea", vm.FeatureType);
        Assert.Equal("42", vm.FeatureRef);
        Assert.Equal("test.000", vm.DatasetFileName);
        Assert.Equal("S-101", vm.ProductSpec);
        Assert.Equal(2, vm.Attributes.Count);
        Assert.True(vm.HasAttributes);
        Assert.Equal("DRVAL1", vm.Attributes[0].Name);
        Assert.Equal("10.0", vm.Attributes[0].Value);
    }

    [Fact]
    public void SetPick_FiltersNullAndWhitespaceAttributeValues()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "Buoy",
            featureRef: "1",
            datasetFileName: null,
            productSpec: "S-101",
            attributes: new Dictionary<string, string?>
            {
                ["A"] = "value",
                ["B"] = null,
                ["C"] = "",
                ["D"] = "   ",
                ["E"] = "another",
            });

        Assert.Equal(2, vm.Attributes.Count);
        Assert.Equal("A", vm.Attributes[0].Name);
        Assert.Equal("E", vm.Attributes[1].Name);
    }

    [Fact]
    public void SetPick_WithEmptyAttributes_ReportsHasAttributesFalse()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "Authority",
            featureRef: "auth.1",
            datasetFileName: "container.gml",
            productSpec: "S-127",
            attributes: new Dictionary<string, string?>());

        Assert.True(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void SetPick_ReplacesPreviousPickContents()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("First", "1", "a.gml", "S-101",
            new Dictionary<string, string?> { ["X"] = "1" });
        vm.SetPick("Second", "2", "b.gml", "S-124",
            new Dictionary<string, string?> { ["Y"] = "2" });

        Assert.Equal("Second", vm.FeatureType);
        Assert.Equal("2", vm.FeatureRef);
        Assert.Equal("b.gml", vm.DatasetFileName);
        Assert.Equal("S-124", vm.ProductSpec);
        Assert.Single(vm.Attributes);
        Assert.Equal("Y", vm.Attributes[0].Name);
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("DepthArea", "42", "test.000", "S-101",
            new Dictionary<string, string?> { ["DRVAL1"] = "10.0" });

        vm.Clear();

        Assert.False(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Null(vm.FeatureType);
        Assert.Null(vm.FeatureRef);
        Assert.Null(vm.DatasetFileName);
        Assert.Null(vm.ProductSpec);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void ClearCommand_InvokesClear()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("X", "1", null, null, new Dictionary<string, string?>());

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

        vm.SetPick("X", "1", null, null, new Dictionary<string, string?>());
        vm.Clear();

        Assert.Equal(2, changed.Count);
    }
}