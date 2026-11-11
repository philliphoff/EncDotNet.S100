using System.ComponentModel;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.ViewModels.Secom;

namespace EncDotNet.S100.Viewer.Views.Secom;

/// <summary>
/// The Identity picker control (#883 B). The flyout follows
/// <see cref="IdentityPickerViewModel.IsOpen"/> both ways, so a status link can
/// open the picker ("Choose identity…").
/// </summary>
public partial class IdentityPicker : UserControl
{
    private IdentityPickerViewModel? _viewModel;

    private Avalonia.Controls.Primitives.FlyoutBase Menu { get; }

    public IdentityPicker()
    {
        InitializeComponent();
        Menu = (Avalonia.Controls.Primitives.FlyoutBase)Field.Flyout!;
        Menu.Opened += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.IsOpen = true;
        };
        Menu.Closed += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.IsOpen = false;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as IdentityPickerViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            Sync();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IdentityPickerViewModel.IsOpen))
            Sync();
    }

    private void Sync()
    {
        if (_viewModel is null)
            return;
        if (_viewModel.IsOpen && !Menu.IsOpen && Field.IsEffectivelyVisible)
            Menu.ShowAt(Field);
        else if (!_viewModel.IsOpen && Menu.IsOpen)
            Menu.Hide();
    }
}
