using System.Globalization;
using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Web;

/// <summary>A toast (handoff D29).</summary>
/// <param name="Key">Unique among the page's toasts.</param>
/// <param name="Tone"><c>info</c> or <c>error</c>.</param>
/// <param name="Icon">The <see cref="Shell.Icon"/> name.</param>
/// <param name="Title">The lead, in bold: "Dataset added:".</param>
/// <param name="Text">The rest: the dataset's id, why it failed.</param>
/// <param name="ActionLabel">The action's label (Show), or <see langword="null"/> for none.</param>
/// <param name="DatasetId">The dataset the action is for.</param>
public sealed record LiveToast(int Key, string Tone, string Icon, string Title, string Text, string? ActionLabel = null, string? DatasetId = null);

/// <summary>
/// What the page shows of the server's state and of changes to what's served
/// (issue #1583, handoff D28, D29, D31): the pill's state and tooltip, the
/// Datasets panel's footer, the toasts and row tags a change brings.
/// </summary>
internal static class ServeLive
{
    /// <summary>How long a dataset added keeps its New tag.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromSeconds(60);

    /// <summary>How long a toast shows; one that failed shows twice as long.</summary>
    public static readonly TimeSpan ToastFor = TimeSpan.FromSeconds(8);

    /// <summary>The most toasts of one kind a change brings before they're counted in one.</summary>
    public const int ToastsPerKind = 3;

    /// <summary>The row tag of a dataset being reloaded.</summary>
    public const string ReloadingTag = "Reloading";

    /// <summary>The row tag of a dataset just added.</summary>
    public const string NewTag = "New";

    /// <summary>The state the server reports (the link's states are the browser's to tell).</summary>
    public static ServerState State(bool updating) => updating ? ServerState.Updating : ServerState.Live;

    /// <summary>A revision as the page shows it: its first 7 characters.</summary>
    public static string? ShortRevision(string? revision) =>
        revision is { Length: > 7 } ? revision[..7] : revision;

    /// <summary>"every 10 s", "every 2 min".</summary>
    public static string Every(TimeSpan interval) =>
        interval.TotalSeconds < 120 || interval.TotalSeconds % 60 != 0
            ? "every " + interval.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
            : "every " + interval.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + " min";

    /// <summary>The pill's tooltip detail (D28): "revision 0878928 · checks files every 10 s"; <see langword="null"/> for a built tile set.</summary>
    public static string? StateDetail(string? revision, TimeSpan? refresh)
    {
        var parts = new List<string>(2);
        if (ShortRevision(revision) is { } shortRevision)
            parts.Add("revision " + shortRevision);
        if (refresh is { } interval)
            parts.Add("checks files " + Every(interval));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The Datasets panel's footer (H2): "Checks files every 10 s · revision 0878928"; <see langword="null"/> without datasets.</summary>
    public static string? Footer(bool hasDatasets, string? revision, TimeSpan? refresh)
    {
        if (!hasDatasets)
            return null;
        var checks = refresh is { } interval ? "Checks files " + Every(interval) : "Doesn't check files for changes";
        return ShortRevision(revision) is { } shortRevision ? checks + " · revision " + shortRevision : checks;
    }

    /// <summary>
    /// The toasts an update brings (D29), without keys: one for each dataset
    /// added (with Show when <paramref name="hasPlace"/> says it can be shown),
    /// removed, or failing to reload, up to <see cref="ToastsPerKind"/> of a
    /// kind and then one counting them. A reload that worked is quiet.
    /// </summary>
    public static IReadOnlyList<LiveToast> Toasts(DatasetsUpdate update, Func<string, bool> hasPlace)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(hasPlace);
        var toasts = new List<LiveToast>();
        void Kind(DatasetChangeKind kind, Func<DatasetChange, LiveToast> one, Func<int, LiveToast> many)
        {
            var changes = update.Changes.Where(c => c.Kind == kind).ToList();
            if (changes.Count > ToastsPerKind)
                toasts.Add(many(changes.Count));
            else
                toasts.AddRange(changes.Select(one));
        }

        Kind(
            DatasetChangeKind.Added,
            c => new LiveToast(0, "info", "circle-plus", "Dataset added:", c.Id ?? c.File ?? "", c.Id is { } id && hasPlace(id) ? "Show" : null, c.Id),
            n => new LiveToast(0, "info", "circle-plus", Count(n) + " datasets added.", "They're tagged New in the list."));
        Kind(
            DatasetChangeKind.Removed,
            c => new LiveToast(0, "info", "circle-minus", "Dataset removed:", c.Id ?? c.File ?? ""),
            n => new LiveToast(0, "info", "circle-minus", Count(n) + " datasets removed.", ""));
        Kind(
            DatasetChangeKind.Failed,
            c => new LiveToast(0, "error", "warn", c.Id is { } id ? "Couldn't reload " + id + "." : "Couldn't reload the datasets.", Reason(c.Reason)),
            n => new LiveToast(0, "error", "warn", "Couldn't reload " + Count(n) + " datasets.", Reason(update.Changes.First(c => c.Kind == DatasetChangeKind.Failed).Reason)));
        return toasts;
    }

    /// <summary>
    /// The row tags after an update (D29): Reloading on the datasets being
    /// reloaded, while the reload lasts; New on those added, until <paramref name="now"/>
    /// passes their time. <paramref name="newUntil"/> is updated in place.
    /// </summary>
    public static Dictionary<string, string> Tags(DatasetsUpdate? reloading, Dictionary<string, DateTime> newUntil, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(newUntil);
        foreach (var (id, until) in newUntil.ToList())
        {
            if (until <= now)
                newUntil.Remove(id);
        }

        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in newUntil.Keys)
            tags[id] = NewTag;
        if (reloading is { InProgress: true })
        {
            foreach (var change in reloading.Changes)
            {
                if (change is { Kind: DatasetChangeKind.Reloading, Id: { } id })
                    tags[id] = ReloadingTag;
            }
        }

        return tags;
    }

    private static string Count(int n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    private static string Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim();
}
