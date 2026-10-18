using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ShadUI;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>The Routes panel's file pickers and dialogs for Import, Copy to edit and Export (#960).</summary>
internal interface IRouteDialogs
{
    /// <summary>Picks a route file to import.</summary>
    /// <param name="patterns">The file patterns the formats read.</param>
    /// <returns>The path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickImportFileAsync(IReadOnlyList<string> patterns);

    /// <summary>Picks where to export a route.</summary>
    /// <param name="suggestedName">The suggested file name, with its extension.</param>
    /// <param name="formatName">The format name.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <returns>The path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickExportFileAsync(string suggestedName, string formatName, string extension);

    /// <summary>Shows a dialog view-model on the main window's dialog host.</summary>
    /// <typeparam name="T">The dialog's view-model type, registered with the dialog host.</typeparam>
    /// <param name="dialog">The view-model.</param>
    /// <param name="maxWidth">The dialog's width.</param>
    void Show<T>(T dialog, double maxWidth)
        where T : class;

    /// <summary>Closes a dialog shown with <see cref="Show{T}"/>.</summary>
    /// <param name="dialog">The view-model.</param>
    void Close(object dialog);
}

/// <summary>The route dialogs on the main window's ShadUI dialog host.</summary>
/// <param name="dialogs">The dialog manager.</param>
/// <param name="files">The native file pickers.</param>
/// <param name="testPick">With <c>--mcp-test-hooks</c> only: the file to answer the import picker with.</param>
/// <param name="testSave">With <c>--mcp-test-hooks</c> only: the file to answer the export picker with.</param>
internal sealed class RouteDialogs(DialogManager dialogs, IFileDialogService files, Func<string?>? testPick = null, Func<string?>? testSave = null) : IRouteDialogs
{
    /// <summary>The variable naming the file the export picker answers with under <c>--mcp-test-hooks</c>.</summary>
    public const string TestSaveVariable = "SOUNDCHARTS_TEST_SAVE_FILE";

    /// <inheritdoc />
    public Task<string?> PickImportFileAsync(IReadOnlyList<string> patterns) =>
        testPick?.Invoke() is { Length: > 0 } path ? Task.FromResult<string?>(path) : files.OpenRouteAsync(MainTopLevel(), patterns);

    /// <inheritdoc />
    public Task<string?> PickExportFileAsync(string suggestedName, string formatName, string extension) =>
        testSave?.Invoke() is { Length: > 0 } path ? Task.FromResult<string?>(path) : files.SaveRouteAsync(MainTopLevel(), suggestedName, formatName, extension);

    /// <inheritdoc />
    public void Show<T>(T dialog, double maxWidth)
        where T : class =>
        dialogs.CreateDialog(dialog).Dismissible().WithMaxWidth(maxWidth).Show();

    /// <inheritdoc />
    public void Close(object dialog) => dialogs.Close(dialog);

    private static TopLevel? MainTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;
}
