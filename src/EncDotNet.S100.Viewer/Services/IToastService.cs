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

    /// <summary>Shows an error toast (longer delay, dismiss on click).</summary>
    void ShowError(string title, string? content = null);

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
