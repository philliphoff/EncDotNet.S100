using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class DisplayToolbarViewModelTests
{
    [Fact]
    public void ActiveCategoryLabel_DefaultsToStandard()
    {
        var state = new EcdisDisplayState();
        using var vm = new DisplayToolbarViewModel(state);

        Assert.Contains("Standard", vm.ActiveCategoryLabel);
        Assert.Equal(EcdisDisplayCategory.Standard, vm.ActiveCategory);
        Assert.True(vm.IsStandard);
        Assert.False(vm.IsDisplayBase);
    }

    [Fact]
    public void SetCategory_UpdatesLabelAndProperties()
    {
        var state = new EcdisDisplayState();
        using var vm = new DisplayToolbarViewModel(state);

        vm.SetCategory(EcdisDisplayCategory.DisplayBase);

        Assert.Equal(EcdisDisplayCategory.DisplayBase, vm.ActiveCategory);
        Assert.True(vm.IsDisplayBase);
        Assert.False(vm.IsStandard);
        Assert.Contains("Display Base", vm.ActiveCategoryLabel);
    }

    [Fact]
    public void SetCategory_SameValue_DoesNotFirePropertyChanged()
    {
        var state = new EcdisDisplayState();
        using var vm = new DisplayToolbarViewModel(state);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetCategory(EcdisDisplayCategory.Standard); // same as default

        Assert.Empty(changed);
    }

    [Fact]
    public void ExternalStateChange_RefreshesProperties()
    {
        var state = new EcdisDisplayState();
        using var vm = new DisplayToolbarViewModel(state);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Mutate state directly (simulating another UI path)
        state.SetCategory(EcdisDisplayCategory.All);

        Assert.Contains(nameof(vm.ActiveCategoryLabel), changed);
        Assert.Contains(nameof(vm.ActiveCategory), changed);
        Assert.True(vm.IsAll);
    }

    [Fact]
    public void CategoryDisplayName_ReturnsLocalizedNames()
    {
        Assert.Equal(Strings.DisplayCategory_DisplayBase, DisplayToolbarViewModel.CategoryDisplayName(EcdisDisplayCategory.DisplayBase));
        Assert.Equal(Strings.DisplayCategory_Standard, DisplayToolbarViewModel.CategoryDisplayName(EcdisDisplayCategory.Standard));
        Assert.Equal(Strings.DisplayCategory_OtherInformation, DisplayToolbarViewModel.CategoryDisplayName(EcdisDisplayCategory.OtherInformation));
        Assert.Equal(Strings.DisplayCategory_All, DisplayToolbarViewModel.CategoryDisplayName(EcdisDisplayCategory.All));
    }

    [Fact]
    public void Commands_SwitchCategory()
    {
        var state = new EcdisDisplayState();
        using var vm = new DisplayToolbarViewModel(state);

        vm.SetDisplayBaseCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.DisplayBase, state.Category);

        vm.SetAllCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.All, state.Category);

        vm.SetOtherInformationCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.OtherInformation, state.Category);

        vm.SetStandardCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.Standard, state.Category);
    }
}
