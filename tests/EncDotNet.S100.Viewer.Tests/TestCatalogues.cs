using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds a <see cref="CatalogueRegistry"/> and the Catalogues activity for tests.</summary>
internal static class TestCatalogues
{
    /// <summary>A registry over fresh managers (or <paramref name="portrayals"/>), not yet initialized.</summary>
    public static CatalogueRegistry Registry(ViewerSettings settings, PortrayalCatalogueManager? portrayals = null) =>
        new(settings, new FeatureCatalogueManager(), portrayals ?? new PortrayalCatalogueManager());

    /// <summary>The Catalogues activity over <paramref name="registry"/>, with stub dialogs and a real notification service.</summary>
    public static CataloguesViewModel ViewModel(
        CatalogueRegistry registry,
        ViewerSettings settings,
        StubCatalogueDialogs? dialogs = null,
        INotificationService? notifications = null,
        ExaminerLinks? examiner = null,
        IReadOnlyDictionary<string, int>? loaded = null,
        Action<string>? openInChartReference = null,
        Action? showLogs = null) =>
        new(
            registry,
            settings,
            dialogs ?? new StubCatalogueDialogs(),
            notifications ?? Notifications.TestNotifications.Create(),
            examiner ?? TestPickReport.Examiner(),
            spec => loaded is not null && loaded.TryGetValue(spec, out var n) ? n : 0,
            openInChartReference,
            showLogs);
}

/// <summary>Answers the catalogue picker from a queue and records the sheets and reveals.</summary>
internal sealed class StubCatalogueDialogs : ICatalogueDialogs
{
    /// <summary>The paths the picker answers with, in order; null (or empty) is a cancel.</summary>
    public Queue<string?> Picks { get; } = new();

    /// <summary>The sheets shown and not closed.</summary>
    public List<object> Open { get; } = [];

    /// <summary>Every sheet shown.</summary>
    public List<object> Shown { get; } = [];

    /// <summary>The paths revealed.</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>The sheet shown last.</summary>
    public AddCatalogueSheetViewModel LastSheet => (AddCatalogueSheetViewModel)Shown[^1];

    public Task<string?> PickCatalogueAsync() => Task.FromResult(Picks.Count > 0 ? Picks.Dequeue() : null);

    public void Show<T>(T sheet)
        where T : class
    {
        Shown.Add(sheet);
        Open.Add(sheet);
    }

    public void Close(object sheet) => Open.Remove(sheet);

    public void Reveal(string path) => Revealed.Add(path);

    public string RevealLabel => "Show in Finder";
}
