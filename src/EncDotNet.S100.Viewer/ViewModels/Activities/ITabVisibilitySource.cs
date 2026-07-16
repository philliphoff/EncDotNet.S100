namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Drives the dynamic visibility of an <see cref="IActivityTab"/> in the
/// activity bar. A tab registered with a visibility source is shown only
/// while <see cref="IsVisible"/> is <see langword="true"/>; tabs without a
/// source are always visible.
/// </summary>
/// <remarks>
/// Implementations must raise <see cref="VisibilityChanged"/> on the UI
/// thread (or accept that <see cref="ActivityTab{TViewModel, TView}"/>
/// marshals the resulting property change when an Avalonia application is
/// running). The first read of <see cref="IsVisible"/> seeds the tab's
/// initial state.
/// </remarks>
internal interface ITabVisibilitySource
{
    /// <summary>Whether the owning tab should currently be visible.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Raised when <see cref="IsVisible"/> changes; the argument is the
    /// new value.
    /// </summary>
    event Action<bool>? VisibilityChanged;
}
