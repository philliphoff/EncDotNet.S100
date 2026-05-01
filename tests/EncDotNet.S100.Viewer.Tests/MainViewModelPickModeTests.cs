using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class MainViewModelPickModeTests
{
    private static MainViewModel CreateViewModel()
    {
        // Construct in-memory settings (without invoking Save()) and a
        // throwaway catalogue manager. MainViewModel only touches the
        // settings file when Save() is called via a setter, which the pick
        // mode commands never do.
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        return new MainViewModel(settings, catalogues);
    }

    [Fact]
    public void IsPickModeActive_DefaultsToFalse()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void TogglePickModeCommand_FlipsState()
    {
        var vm = CreateViewModel();

        vm.TogglePickModeCommand.Execute(null);
        Assert.True(vm.IsPickModeActive);

        vm.TogglePickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void ExitPickModeCommand_TurnsOffAndIsIdempotent()
    {
        var vm = CreateViewModel();
        vm.IsPickModeActive = true;

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void IsPickModeActive_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPickModeActive))
                fired++;
        };

        vm.TogglePickModeCommand.Execute(null);
        vm.TogglePickModeCommand.Execute(null);

        Assert.Equal(2, fired);
    }
}
