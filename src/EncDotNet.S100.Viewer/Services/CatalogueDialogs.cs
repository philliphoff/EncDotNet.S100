using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EncDotNet.S100.Viewer.Resources;
using ShadUI;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What the Catalogues activity needs from the desktop (design note
/// <c>catalogue-panels.md</c> §6.2, D11): the catalogue picker, the Add
/// catalogue sheet's host and the file manager.
/// </summary>
internal interface ICatalogueDialogs
{
    /// <summary>Picks a feature catalogue, a <c>portrayal_catalogue.xml</c> or a catalogue package.</summary>
    /// <returns>The path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickCatalogueAsync();

    /// <summary>Shows the Add catalogue sheet.</summary>
    /// <typeparam name="T">The sheet's view model type, registered with the dialog host.</typeparam>
    /// <param name="sheet">The sheet's view model.</param>
    void Show<T>(T sheet)
        where T : class;

    /// <summary>Closes a sheet shown with <see cref="Show{T}"/>.</summary>
    /// <param name="sheet">The sheet's view model.</param>
    void Close(object sheet);

    /// <summary>Shows <paramref name="path"/> in Finder, Explorer or the file manager.</summary>
    /// <param name="path">A file or folder.</param>
    void Reveal(string path);

    /// <summary>The label for <see cref="Reveal"/> on this platform: Show in Finder, Explorer or folder.</summary>
    string RevealLabel { get; }
}

/// <summary>The catalogue dialogs on the main window's ShadUI dialog host.</summary>
/// <param name="dialogs">The dialog manager.</param>
/// <param name="files">The native file pickers.</param>
internal sealed class CatalogueDialogs(DialogManager dialogs, IFileDialogService files) : ICatalogueDialogs
{
    /// <summary>The width of the Add catalogue sheet.</summary>
    public const double SheetWidth = 480;

    /// <inheritdoc />
    public Task<string?> PickCatalogueAsync() => files.OpenCatalogueAsync(MainTopLevel());

    /// <inheritdoc />
    public void Show<T>(T sheet)
        where T : class =>
        dialogs.CreateDialog(sheet).Dismissible().WithMaxWidth(SheetWidth).Show();

    /// <inheritdoc />
    public void Close(object sheet) => dialogs.Close(sheet);

    /// <inheritdoc />
    public string RevealLabel => RevealLabelFor();

    /// <summary>The Show in … label for the current platform.</summary>
    public static string RevealLabelFor() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? Strings.Catalogues_ShowInFinder
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Strings.Catalogues_ShowInExplorer
        : Strings.Catalogues_ShowInFolder;

    /// <inheritdoc />
    public void Reveal(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", ["-R", path]);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Process.Start(new ProcessStartInfo("xdg-open", folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Best effort: the path is shown in the details pane.
        }
    }

    private static TopLevel? MainTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;
}
