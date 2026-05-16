namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Abstraction for showing toast notifications in the viewer UI.
/// Decouples background services from the concrete ShadUI
/// <c>ToastManager</c> implementation.
/// </summary>
internal interface IToastService
{
    /// <summary>Shows an informational toast.</summary>
    void ShowInfo(string title, string? content = null);

    /// <summary>Shows a success toast with a short auto-dismiss delay.</summary>
    void ShowSuccess(string title, string? content = null);

    /// <summary>Shows a warning toast.</summary>
    void ShowWarning(string title, string? content = null);

    /// <summary>
    /// Shows an error toast. When <paramref name="actionLabel"/> and
    /// <paramref name="action"/> are both supplied, the toast renders an
    /// action button that invokes <paramref name="action"/> on click.
    /// When <paramref name="sticky"/> is true the toast does not
    /// auto-dismiss; the user must click it (or the action button) to
    /// remove it. Used by the dataset loader to surface a structured
    /// short error message with a "Copy details" action.
    /// </summary>
    void ShowError(
        string title,
        string? content = null,
        string? actionLabel = null,
        Action? action = null,
        bool sticky = false);

    /// <summary>
    /// Shows a persistent loading toast with an action button (e.g.
    /// "Cancel"). The toast stays visible until explicitly dismissed via
    /// <see cref="DismissAll"/> or the action button is clicked (ShadUI
    /// auto-dismisses after an action click).
    /// </summary>
    void ShowLoading(string title, string? content = null, string? actionLabel = null, Action? action = null);

    /// <summary>
    /// Dismisses all currently visible toasts. Use after a long-running
    /// operation completes to clear loading toasts before showing the
    /// result toast.
    /// </summary>
    void DismissAll();
}
