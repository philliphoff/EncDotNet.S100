namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>
/// The dialogs and file pickers the Keys &amp; certificates page opens (#845),
/// behind an interface so the page's logic is testable without a window.
/// </summary>
internal interface IKeysDialogs
{
    /// <summary>Asks for a .p12, .pfx or PEM identity file; <see langword="null"/> when cancelled.</summary>
    Task<string?> PickIdentityFileAsync();

    /// <summary>Asks for a PEM certificate file; <see langword="null"/> when cancelled.</summary>
    Task<string?> PickAuthorityFileAsync();

    /// <summary>Shows a dialog view model registered with the dialog manager (by its type, hence generic).</summary>
    void Show<T>(T dialog, double maxWidth)
        where T : class;

    /// <summary>Closes a dialog shown with <see cref="Show{T}"/>.</summary>
    void Close(object dialog);

    /// <summary>Asks to confirm a destructive action, running <paramref name="confirmed"/> on yes.</summary>
    void Confirm(string title, string message, string confirmLabel, Action confirmed);
}
