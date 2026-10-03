using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Joins the Library panel's Timeline actions to the Timeline (#711, handoff
/// G3): "Show on timeline" opens the dock on the dataset's window, "Go to
/// start of run" pins the view time there, and "Reveal in Library" from a
/// Timeline band shows the Library panel.
/// </summary>
internal sealed class LibraryTimelineLink
{
    public LibraryTimelineLink(LibraryPanelViewModel library, TimelineViewModel timeline, GlobalTimeService time, MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(main);
        library.ShowOnTimelineRequested += (_, window) =>
        {
            main.IsBottomDockOpen = true;
            timeline.IsCollapsedToStrip = false;
            timeline.SetWindow(window.Start, window.End);
        };
        library.GoToTimeRequested += (_, at) =>
        {
            main.IsBottomDockOpen = true;
            time.SetCurrentTime(at);
        };
        library.Revealed += (_, _) => main.SelectTab(LibraryImportCoordinator.LibraryPanelId);
    }
}
