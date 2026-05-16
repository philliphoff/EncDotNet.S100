using ShadUI;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IToastService"/> implementation backed by the
/// ShadUI <see cref="ToastManager"/>. All toasts are positioned at the
/// bottom-right corner to stay out of the map viewport.
/// </summary>
internal sealed class ToastService : IToastService
{
    private readonly ToastManager _manager;

    /// <summary>Default auto-dismiss delay for brief confirmations (seconds).</summary>
    private const double ShortDelay = 4;

    /// <summary>Default auto-dismiss delay for warnings/info (seconds).</summary>
    private const double MediumDelay = 8;

    /// <summary>Default auto-dismiss delay for errors (seconds).</summary>
    private const double LongDelay = 15;

    /// <summary>
    /// Effectively-infinite delay used for sticky toasts that must
    /// stay on screen until the user dismisses them. ShadUI takes the
    /// delay in seconds; 365 days is large enough to never trigger in
    /// any normal session and small enough not to risk overflow.
    /// </summary>
    private const double StickyDelay = 60.0 * 60.0 * 24.0 * 365.0;

    /// <summary>Long delay for loading toasts so they stay visible during the operation.</summary>
    private const double LoadingDelay = 300;

    public ToastService(ToastManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    /// <inheritdoc />
    public void ShowInfo(string title, string? content = null)
    {
        var builder = _manager.CreateToast(title)
            .OnBottomRight()
            .WithDelay(MediumDelay)
            .DismissOnClick();

        if (content is not null)
            builder = builder.WithContent(content);

        builder.ShowInfo();
    }

    /// <inheritdoc />
    public void ShowSuccess(string title, string? content = null)
    {
        var builder = _manager.CreateToast(title)
            .OnBottomRight()
            .WithDelay(ShortDelay)
            .DismissOnClick();

        if (content is not null)
            builder = builder.WithContent(content);

        builder.ShowSuccess();
    }

    /// <inheritdoc />
    public void ShowWarning(string title, string? content = null)
    {
        var builder = _manager.CreateToast(title)
            .OnBottomRight()
            .WithDelay(MediumDelay)
            .DismissOnClick();

        if (content is not null)
            builder = builder.WithContent(content);

        builder.ShowWarning();
    }

    /// <inheritdoc />
    public void ShowError(
        string title,
        string? content = null,
        string? actionLabel = null,
        Action? action = null,
        bool sticky = false)
    {
        var builder = _manager.CreateToast(title)
            .OnBottomRight()
            .WithDelay(sticky ? StickyDelay : LongDelay)
            .DismissOnClick();

        if (content is not null)
            builder = builder.WithContent(content);

        if (actionLabel is not null && action is not null)
            builder = builder.WithAction(actionLabel, action);

        builder.ShowError();
    }

    /// <inheritdoc />
    public void ShowLoading(string title, string? content = null, string? actionLabel = null, Action? action = null)
    {
        var builder = _manager.CreateToast(title)
            .OnBottomRight()
            .WithDelay(LoadingDelay)
            .DismissOnClick();

        if (content is not null)
            builder = builder.WithContent(content);

        if (actionLabel is not null && action is not null)
            builder = builder.WithAction(actionLabel, action);

        builder.ShowInfo();
    }

    /// <inheritdoc />
    public void DismissAll() => _manager.DismissAll();
}
