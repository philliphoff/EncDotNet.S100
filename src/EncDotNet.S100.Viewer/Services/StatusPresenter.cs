using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IStatusPresenter"/> implementation. Reuses
/// <see cref="ViewModelBase"/> for <c>INotifyPropertyChanged</c>
/// plumbing.
/// </summary>
internal sealed class StatusPresenter : ViewModelBase, IStatusPresenter
{
    private string? _statusText;

    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
