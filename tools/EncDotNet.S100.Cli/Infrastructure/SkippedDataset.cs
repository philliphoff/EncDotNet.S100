namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>Why a dataset that was found isn't served or rendered.</summary>
internal enum SkippedDatasetKind
{
    /// <summary>It was recognised but couldn't be opened, or can't be drawn.</summary>
    Failed,

    /// <summary>It's protected (S-100 Part 15), and there are no keys to open it.</summary>
    Protected,

    /// <summary>No product claims it, or it's of a kind that's left alone (an archive, an orphan update).</summary>
    Unsupported,

    /// <summary>A catalogue or a list names it, but the file isn't there.</summary>
    Missing,
}

/// <summary>
/// A dataset that was found but skipped (issue #1583): kept by a <c>tiles serve</c>
/// session so its web page can list it, besides the warning printed for it.
/// </summary>
/// <param name="Path">The dataset's path, as found.</param>
/// <param name="Kind">Why it was skipped.</param>
/// <param name="Reason">Why, in one sentence for a person: "Failed to load: …", "Skipped: protected (S-100 Part 15). Serve it decrypted."</param>
/// <param name="Warning">The warning printed for it, as before.</param>
internal sealed record SkippedDataset(string Path, SkippedDatasetKind Kind, string Reason, string Warning)
{
    /// <summary>The file name the page shows.</summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : Path;

    /// <summary>A dataset no product claims.</summary>
    /// <param name="path">The file.</param>
    /// <param name="shown">The path as the warning names it, when not <paramref name="path"/>.</param>
    public static SkippedDataset Unrecognized(string path, string? shown = null) => new(
        path,
        SkippedDatasetKind.Unsupported,
        "Skipped: not a dataset of a product s100 reads.",
        UnrecognizedDatasetMessage.Skipped(path, shown));

    /// <summary>A protected dataset.</summary>
    /// <param name="path">The file.</param>
    /// <param name="shown">The path as the warning names it.</param>
    public static SkippedDataset Protected(string path, string shown) => new(
        path,
        SkippedDatasetKind.Protected,
        "Skipped: protected (S-100 Part 15). Serve it decrypted.",
        $"Skipped data-protected (encrypted) dataset: {shown}");

    /// <summary>A dataset whose file isn't there.</summary>
    /// <param name="path">The file.</param>
    /// <param name="shown">The path as the warning names it.</param>
    public static SkippedDataset Missing(string path, string shown) => new(
        path,
        SkippedDatasetKind.Missing,
        "Skipped: the file is missing.",
        $"Skipped missing dataset file: {shown}");

    /// <summary>A dataset that was recognised but couldn't be opened.</summary>
    /// <param name="path">The file.</param>
    /// <param name="spec">Its product.</param>
    /// <param name="message">What went wrong.</param>
    public static SkippedDataset FailedToOpen(string path, string spec, string message) => new(
        path,
        SkippedDatasetKind.Failed,
        "Failed to load: " + Sentence(message),
        $"Skipped {spec} dataset that could not be opened: {path} ({message})");

    /// <summary>A dataset of a product that can't be drawn as tiles.</summary>
    /// <param name="path">The file.</param>
    /// <param name="spec">Its product.</param>
    public static SkippedDataset NotTileable(string path, string spec) => new(
        path,
        SkippedDatasetKind.Unsupported,
        $"Skipped: {spec} can't be drawn as tiles.",
        $"Skipped {spec} dataset, which can't be tiled: {path}");

    // "record 1 412 has a broken pointer" → "record 1 412 has a broken pointer."
    private static string Sentence(string message)
    {
        var text = message.Trim();
        return text.Length == 0 ? "an unknown error." : text.EndsWith('.') ? text : text + ".";
    }
}
