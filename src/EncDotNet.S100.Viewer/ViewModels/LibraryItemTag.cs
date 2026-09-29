using System.Windows.Input;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>What a <see cref="LibraryItemTag"/> says about a dataset (its style).</summary>
internal enum LibraryItemTagKind
{
    /// <summary>A newer edition or update is available (amber tint).</summary>
    Update,

    /// <summary>Open in the viewer (the only filled tag).</summary>
    Loaded,

    /// <summary>Will load when it comes into view (outline).</summary>
    OnPan,

    /// <summary>Waiting in a bulk download (muted).</summary>
    Queued,

    /// <summary>The last download failed; clicking retries (red tint).</summary>
    Failed,

    /// <summary>A neutral fact, e.g. a community-list package (outline).</summary>
    Neutral,
}

/// <summary>
/// A secondary state of a Library dataset — what is happening to it — shown
/// as a small sentence-case tag after its name. A dataset has zero or more.
/// </summary>
/// <param name="Text">The tag text.</param>
/// <param name="Kind">How the tag is styled.</param>
/// <param name="Command">What clicking the tag does (retrying a failed download), if anything.</param>
internal sealed record LibraryItemTag(string Text, LibraryItemTagKind Kind, ICommand? Command = null)
{
    public bool IsUpdate => Kind == LibraryItemTagKind.Update;

    public bool IsLoaded => Kind == LibraryItemTagKind.Loaded;

    public bool IsOutline => Kind is LibraryItemTagKind.OnPan or LibraryItemTagKind.Neutral;

    public bool IsQueued => Kind == LibraryItemTagKind.Queued;

    public bool IsFailed => Kind == LibraryItemTagKind.Failed;

    public bool IsClickable => Command is not null;
}
