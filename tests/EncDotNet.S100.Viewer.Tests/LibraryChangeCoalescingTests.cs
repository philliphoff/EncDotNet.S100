using Avalonia.Threading;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A burst of Library changes (one per cell a pan evicts or loads) must refresh
/// the Library panel and the Timeline once, not once per cell.
/// </summary>
public sealed class LibraryChangeCoalescingTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();
    private readonly Queue<Action> _posted = new();

    public LibraryChangeCoalescingTests()
    {
        _library = _context.CreateService();
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private void Post(Action action) => _posted.Enqueue(action);

    private void RunPosted()
    {
        while (_posted.TryDequeue(out var action))
            action();
    }

    [Fact]
    public void A_burst_of_loader_changes_refreshes_the_panel_once()
    {
        using var panel = new LibraryPanelViewModel(
            _library, new RecordingLibraryImporter(), _loader, _downloader, action => action(), coalesce: Post, time: TimeProvider.System);

        for (var i = 0; i < 50; i++)
            _loader.RaiseChanged();

        Assert.Single(_posted);
        RunPosted();

        // A later change refreshes again.
        _loader.RaiseChanged();
        Assert.Single(_posted);
    }

    [Fact]
    public void A_burst_of_loader_changes_raises_the_timeline_change_once()
    {
        using var panel = new LibraryPanelViewModel(
            _library, new RecordingLibraryImporter(), new FakeLibraryLoader(), _downloader, action => action(), coalesce: action => action(), time: TimeProvider.System);
        var source = new LibraryTimeSource(panel, _library, _loader, TimeProvider.System, Post);
        var changes = 0;
        source.Changed += () => changes++;

        for (var i = 0; i < 50; i++)
            _loader.RaiseChanged();
        RunPosted();

        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void On_the_ui_thread_the_default_dispatch_still_coalesces()
    {
        // The defaults used to run the refresh inline on the UI thread, which
        // cleared the coalescing flag before the next change arrived.
        using var panel = new LibraryPanelViewModel(
            _library, new RecordingLibraryImporter(), new FakeLibraryLoader(), _downloader,
            EncDotNet.S100.Viewer.Services.UiThread.RunOrPost, EncDotNet.S100.Viewer.Services.UiThread.Post, TimeProvider.System);
        var source = new LibraryTimeSource(panel, _library, _loader, TimeProvider.System, dispatch: EncDotNet.S100.Viewer.Services.UiThread.Post);
        var changes = 0;
        source.Changed += () => changes++;

        for (var i = 0; i < 50; i++)
            _loader.RaiseChanged();
        Assert.Equal(0, changes);

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, changes);
    }
}
