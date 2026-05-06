using System.ComponentModel;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// App-wide status-bar text presenter. Decouples background services
/// (pick, dataset loader, etc.) from the root <c>MainViewModel</c>:
/// services depend on this small interface; <c>MainViewModel</c>
/// composes one and forwards its <c>StatusText</c> property so the
/// existing XAML binding keeps working.
/// </summary>
internal interface IStatusPresenter : INotifyPropertyChanged
{
    /// <summary>
    /// The status-bar text shown at the bottom of the main window.
    /// Setting this property raises <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// so view-models forwarding the value can re-notify their bound view.
    /// </summary>
    string? StatusText { get; set; }
}
