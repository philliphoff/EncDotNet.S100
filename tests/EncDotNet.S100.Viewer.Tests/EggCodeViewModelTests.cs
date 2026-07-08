using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class EggCodeViewModelTests
{
    [Fact]
    public void TrailingRows_ExposeFourthClassOutsideOval()
    {
        var egg = IceEggCodeBuilder.Build("70", "[30, 30, 10, 4]", "[91, 87, 85, 95]", "[5, 4, 4]")!;

        var vm = new EggCodeViewModel(egg);

        Assert.True(vm.ShowTrailingStagesOfDevelopment);
        Assert.Equal(new[] { "Sd" }, vm.TrailingStagesOfDevelopment.Select(v => v.Symbol));
        Assert.Equal(new[] { "95" }, vm.TrailingStagesOfDevelopment.Select(v => v.Text));
        Assert.True(vm.ShowTrailingPartialConcentrations);
        Assert.Equal(new[] { "4" }, vm.TrailingPartialConcentrations.Select(v => v.Text));
        Assert.False(vm.ShowTrailingFormsOfIce);
    }

    [Fact]
    public void SnowSummary_FormatsCentimetres()
    {
        var egg = IceEggCodeBuilder.Build("70", "[30, 40]", "[91, 87]", "[5, 4]", snowDepthCm: 12.5)!;

        var vm = new EggCodeViewModel(egg);

        Assert.True(vm.ShowSnowSummary);
        Assert.Contains("12.5", vm.SnowSummary);
    }

    [Fact]
    public void TraceSummary_PresentWhenTraceFlagged()
    {
        var egg = IceEggCodeBuilder.Build("70", "[30, 40]", "[91, 87]", "[5, 4]", traceOfIce: true)!;

        var vm = new EggCodeViewModel(egg);

        Assert.True(vm.TraceOfIce);
        Assert.NotNull(vm.TraceSummary);
    }

    [Fact]
    public void NoAnnotations_LeavesSummariesNull()
    {
        var egg = IceEggCodeBuilder.Build("70", "[30, 40]", "[91, 87]", "[5, 4]")!;

        var vm = new EggCodeViewModel(egg);

        Assert.False(vm.HasAnnotations);
        Assert.Null(vm.SnowSummary);
        Assert.Null(vm.TraceSummary);
    }

    [Fact]
    public void OpenWater_HasNoOvalAndShowsTotalConcentration()
    {
        var egg = IceEggCodeBuilder.Build("0", null, null, null)!;

        var vm = new EggCodeViewModel(egg);

        Assert.False(vm.HasOval);
        Assert.True(vm.ShowTotalConcentration);
        Assert.Equal("0", vm.TotalConcentration!.Text);
    }
}
