using System.Windows.Input;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A secondary state of a Library dataset — what is happening to it — shown
/// as a small sentence-case tag after its name. A dataset has zero or more.
/// </summary>
/// <param name="Text">The tag text.</param>
/// <param name="Kind">How the tag is styled.</param>
/// <param name="Command">What clicking the tag does (retrying a failed download), if anything.</param>
internal sealed record LibraryItemTag(string Text, LibraryTagKind Kind, ICommand? Command = null)
{
    public bool IsUpdate => Kind == LibraryTagKind.Update;

    public bool IsLoaded => Kind == LibraryTagKind.Loaded;

    public bool IsOutline => Kind is LibraryTagKind.OnPan or LibraryTagKind.Neutral;

    public bool IsQueued => Kind == LibraryTagKind.Queued;

    public bool IsFailed => Kind is LibraryTagKind.Failed or LibraryTagKind.Expired;

    public bool IsClickable => Command is not null;
}
