using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels.Keys;
using ShadUI;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>The Keys &amp; certificates page's dialogs (#845), on the main window's ShadUI dialog host.</summary>
/// <param name="dialogs">The main window's dialog manager.</param>
/// <param name="files">The native file pickers.</param>
/// <param name="testPick">
/// With <c>--mcp-test-hooks</c> only: a file to answer pickers with instead
/// of showing them (scripted UI automation cannot drive a native picker).
/// </param>
internal sealed class KeysDialogs(DialogManager dialogs, IFileDialogService files, Func<string?>? testPick = null) : IKeysDialogs
{
    /// <summary>The variable naming the file pickers answer with under <c>--mcp-test-hooks</c>.</summary>
    public const string TestPickVariable = "SOUNDCHARTS_TEST_PICK_FILE";

    /// <inheritdoc />
    public Task<string?> PickIdentityFileAsync() =>
        testPick?.Invoke() is { Length: > 0 } path ? Task.FromResult<string?>(path) : files.OpenSecomIdentityAsync(MainTopLevel());

    /// <inheritdoc />
    public Task<string?> PickAuthorityFileAsync() =>
        testPick?.Invoke() is { Length: > 0 } path ? Task.FromResult<string?>(path) : files.OpenCertificateAsync(MainTopLevel());

    /// <inheritdoc />
    public void Show<T>(T dialog, double maxWidth)
        where T : class =>
        dialogs.CreateDialog(dialog).Dismissible().WithMaxWidth(maxWidth).Show();

    /// <inheritdoc />
    public void Close(object dialog) => dialogs.Close(dialog);

    /// <inheritdoc />
    public void Confirm(string title, string message, string confirmLabel, Action confirmed) =>
        dialogs.CreateDialog(title, message)
            .WithPrimaryButton(confirmLabel, confirmed, DialogButtonStyle.Destructive)
            .WithCancelButton(Strings.Button_Cancel)
            .WithMaxWidth(440)
            .Dismissible()
            .Show();

    private static TopLevel? MainTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;
}
