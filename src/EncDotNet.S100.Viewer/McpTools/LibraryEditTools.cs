using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer-only tools that change the Library through the code paths the UI
// uses (#715 slice 3).

/// <summary>Previews or adds a Library source (MCP <c>add_library_source</c>).</summary>
internal sealed class AddLibrarySourceTool(IViewerLibraryEditor editor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "add_library_source";

    private readonly IViewerLibraryEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<AddSourceResult>> InvokeAsync(AddSourceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Result(await _editor.AddSourceAsync(request, ct).ConfigureAwait(false));
    }

    internal static ToolResult<T> Result<T>(EditOutcome<T> outcome) =>
        outcome.Error is { } error ? ToolResult<T>.Err(error) : ToolResult<T>.Ok(outcome.Value!);
}

/// <summary>Re-indexes Library sources and reports what changed (MCP <c>refresh_library_source</c>).</summary>
internal sealed class RefreshLibrarySourceTool(IViewerLibraryEditor editor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "refresh_library_source";

    /// <summary>How long to wait for indexing when no wait is given.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);

    /// <summary>The longest wait allowed.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    private readonly IViewerLibraryEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    /// <summary>Refreshes.</summary>
    public async Task<ToolResult<RefreshResult>> InvokeAsync(string? id, int? waitMs, CancellationToken ct = default)
    {
        Guid? target = null;
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (!Guid.TryParse(id.Trim(), out var parsed))
                return ToolResult<RefreshResult>.Err(new InvalidArgument("id", "expected a collection or source id from list_library_sources"));
            target = parsed;
        }
        var wait = waitMs is { } ms ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, (int)MaxWait.TotalMilliseconds)) : DefaultWait;
        return AddLibrarySourceTool.Result(await _editor.RefreshAsync(target, wait, ct).ConfigureAwait(false));
    }
}

/// <summary>Request for <see cref="LibraryActionTool"/>.</summary>
internal sealed record LibraryActionToolRequest(
    string Action,
    IReadOnlyList<string>? ItemIds,
    QueryLibraryItemsRequest Filter,
    bool? All,
    bool? DryRun,
    long? MaxBytes);

/// <summary>Loads, downloads, updates or cancels Library items (MCP <c>library_action</c>).</summary>
internal sealed class LibraryActionTool(IViewerLibraryEditor editor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "library_action";

    private readonly IViewerLibraryEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<LibraryActionResult>> InvokeAsync(LibraryActionToolRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;

        if (request.All == true)
        {
            if (action != "cancel" || request.ItemIds is { Count: > 0 } || QueryLibraryItemsTool.HasFilter(request.Filter))
                return ToolResult<LibraryActionResult>.Err(new InvalidArgument("all", "only for action 'cancel', without itemIds or filters"));
            if (request.DryRun == true)
                return ToolResult<LibraryActionResult>.Ok(new LibraryActionResult("cancel", true, 0, new Dictionary<string, int>(), 0, 0, [], null, false));
            await _editor.CancelAllDownloadsAsync(ct).ConfigureAwait(false);
            return ToolResult<LibraryActionResult>.Ok(new LibraryActionResult("cancel", false, 0, new Dictionary<string, int>(), 0, 0, [], null, false));
        }

        if (request.MaxBytes is < 0)
            return ToolResult<LibraryActionResult>.Err(new InvalidArgument("maxBytes", "must be 0 or more"));

        LibraryItemQuery? filter = null;
        if (QueryLibraryItemsTool.HasFilter(request.Filter))
        {
            var (query, error) = QueryLibraryItemsTool.Parse(request.Filter);
            if (error is not null)
                return ToolResult<LibraryActionResult>.Err(error);
            filter = query;
        }

        return AddLibrarySourceTool.Result(await _editor.ActAsync(
            new LibraryActionRequest(action, request.ItemIds, filter, request.DryRun == true, request.MaxBytes), ct).ConfigureAwait(false));
    }
}

/// <summary>Removes a Library collection or source (MCP <c>remove_library_source</c>).</summary>
internal sealed class RemoveLibrarySourceTool(IViewerLibraryEditor editor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "remove_library_source";

    private readonly IViewerLibraryEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    /// <summary>Removes it, when <paramref name="confirm"/> is true.</summary>
    public async Task<ToolResult<RemoveSourceResult>> InvokeAsync(string id, bool? confirm, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id.Trim(), out var target))
            return ToolResult<RemoveSourceResult>.Err(new InvalidArgument("id", "expected a collection or source id from list_library_sources"));
        if (confirm != true)
            return ToolResult<RemoveSourceResult>.Err(new InvalidArgument("confirm", "must be true to remove; removing cannot be undone from here"));
        return AddLibrarySourceTool.Result(await _editor.RemoveAsync(target, ct).ConfigureAwait(false));
    }
}

/// <summary>Waits for Library indexing and downloads (MCP <c>await_library_idle</c>).</summary>
internal sealed class AwaitLibraryIdleTool(IViewerLibraryEditor editor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "await_library_idle";

    private readonly IViewerLibraryEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));

    /// <summary>Waits.</summary>
    public async Task<ToolResult<LibraryIdleResult>> InvokeAsync(int? timeoutMs, CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
            timeoutMs ?? (int)RefreshLibrarySourceTool.DefaultWait.TotalMilliseconds,
            0,
            (int)RefreshLibrarySourceTool.MaxWait.TotalMilliseconds));
        return ToolResult<LibraryIdleResult>.Ok(await _editor.AwaitIdleAsync(timeout, ct).ConfigureAwait(false));
    }
}
