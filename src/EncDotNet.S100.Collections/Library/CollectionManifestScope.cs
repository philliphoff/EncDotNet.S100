using System.Globalization;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// A scope of a local collection manifest (<c>*.s100collection.json</c>): by
/// group. A manifest that cannot be read reports every problem in it
/// (<see cref="Problems"/>) rather than a single error.
/// </summary>
public sealed class CollectionManifestScope : LibraryCatalogueScope
{
    private CollectionManifestDocument? _manifest;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];
    private IReadOnlyList<string> _problems = [];
    private string _path;

    /// <summary>Creates a scope of the manifest at <paramref name="path"/>.</summary>
    /// <param name="path">The manifest's full path.</param>
    public CollectionManifestScope(string path)
        : base(new Uri(Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)))))
    {
        _path = path;
    }

    /// <summary>The manifest's path; changing it means reading another manifest (picks whose ids still exist are kept).</summary>
    public string ManifestPath
    {
        get => _path;
        set => _path = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Group ids to tick when the manifest is next read, in place of the current ticks (editing a source's groups).</summary>
    public IReadOnlyCollection<string>? PendingPicks { get; set; }

    /// <inheritdoc />
    public override bool IsLoaded => _manifest is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>Every group of the manifest.</summary>
    public IReadOnlyList<LibraryChoice> ManifestGroups => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>One line per problem that makes the manifest unreadable ("line 12 · groups[5].id: …").</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The manifest's title, or its file name without the suffix.</summary>
    public string Title => _manifest?.Title ?? LibrarySourceText.DefaultName(_path);

    /// <inheritdoc />
    public override string? Name => Title;

    /// <summary>The filter for the ticked groups (all of them when everything is included).</summary>
    public LocalManifestFilter CurrentFilter => IncludeAll
        ? LocalManifestFilter.All
        : new LocalManifestFilter { Groups = ManifestGroups.Where(o => o.IsSelected).Select(o => o.Value).ToArray() };

    /// <summary>"IC-ENC", or "IC-ENC — Belgium and Germany" for a selection.</summary>
    public string AutoName => IncludeAll || DescribeSelection() is not { } selection ? Title : $"{Title} — {selection}";

    /// <summary>Names of the groups that matter and are missing on disk: the ticked ones, or all of them for everything.</summary>
    public IReadOnlyList<string> MissingInScope =>
        [.. ManifestGroups.Where(o => o.IsMissing && (IncludeAll || o.IsSelected)).Select(o => o.Label)];

    /// <summary>True when the summary is a warning: nothing ticked, or missing groups in scope.</summary>
    public bool IsSummaryWarning => (!IncludeAll && SelectedCount == 0) || MissingInScope.Count > 0;

    /// <inheritdoc />
    public override bool CanBuild => _manifest is not null && _problems.Count == 0 && (IncludeAll || SelectedCount > 0);

    /// <inheritdoc />
    public override async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Keep the picks across a reload (or a different file) where the ids still exist.
        var picks = PendingPicks ?? ManifestGroups.Where(o => o.IsSelected).Select(o => o.Value).ToArray();
        PendingPicks = null;

        var path = _path;
        var (manifest, summary, problems) = await Task.Run(() => Read(path), cancellationToken).ConfigureAwait(false);
        _manifest = manifest;
        _problems = problems;
        if (manifest is null)
        {
            _groups = [];
            return string.Join("; ", problems);
        }

        var selected = new HashSet<string>(picks, StringComparer.OrdinalIgnoreCase);
        _groups =
        [
            new(LibraryText.Get("Manifest_GroupsTitle"),
                [.. manifest.Groups.Zip(summary).Select(p => new LibraryChoice(p.First.Id, p.First.DisplayName, string.Empty)
                {
                    PathText = p.First.Paths[0],
                    MorePathsText = p.First.Paths.Count > 1
                        ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_MorePathsFormat"), p.First.Paths.Count - 1)
                        : null,
                    PathsTooltip = string.Join(Environment.NewLine, p.First.Paths),
                    IsMissing = p.Second.MissingPathCount > 0,
                    IsSelected = selected.Contains(p.First.Id),
                })]),
        ];
        return null;

        static (CollectionManifestDocument?, IReadOnlyList<CollectionManifestGroupSummary>, IReadOnlyList<string>) Read(string path)
        {
            try
            {
                var manifest = CollectionManifest.ReadFile(path);
                return (manifest, CollectionManifest.Summarize(manifest, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!), []);
            }
            catch (CollectionManifestException ex)
            {
                return (null, [], ex.Problems.Select(Describe).ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException or NotSupportedException)
            {
                return (null, [], [ex.Message]);
            }
        }

        static string Describe(CollectionManifestProblem problem) => problem.Line is { } line
            ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_ErrorLineFormat"), line, problem)
            : problem.ToString();
    }

    /// <inheritdoc />
    public override string SelectionSummary => _manifest is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_SelectionOfFormat"), SelectedCount, ManifestGroups.Count);

    /// <inheritdoc />
    public override string EverythingSummary => ManifestGroups.Count == 1
        ? LibraryText.Get("Manifest_EverythingOne")
        : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_EverythingFormat"), ManifestGroups.Count);

    /// <inheritdoc />
    public override string ScopeSummary
    {
        get
        {
            if (!IncludeAll && SelectedCount == 0)
                return LibraryText.Get("Manifest_SummaryNothing");

            if (MissingInScope is { Count: > 0 } missing)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    LibraryText.Get(missing.Count == 1 ? "Manifest_SummaryMissingOneFormat" : "Manifest_SummaryMissingManyFormat"),
                    JoinNames(missing));
            }

            if (IncludeAll)
                return LibraryText.Get("Manifest_SummaryEverything");

            var names = string.Join(", ", ManifestGroups.Where(o => o.IsSelected).Select(o => o.Label));
            var count = SelectedCount;
            return count == 1
                ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_SummaryOneFormat"), names)
                : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_SummaryManyFormat"), count, names);
        }
    }

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry => null;

    /// <summary>"Belgium", "Belgium and Germany", "Belgium, Germany and 2 more"; null when nothing is ticked.</summary>
    public override string? DescribeSelection()
    {
        var labels = ManifestGroups.Where(o => o.IsSelected).Select(o => o.Label).ToArray();
        return labels.Length == 0 ? null : JoinNames(labels);
    }

    /// <summary>The manifest source, named <paramref name="name"/> (or <see cref="AutoName"/> when null).</summary>
    /// <param name="id">The new source's id.</param>
    /// <param name="name">The source's name: a new collection's, so the two read alike.</param>
    public override CollectionSource Build(Guid id, string? name) =>
        new LocalManifestSource(id, string.IsNullOrWhiteSpace(name) ? AutoName : name.Trim(), _path, CurrentFilter);

    /// <summary>"Belgium", "Belgium and Germany", "Belgium, Germany and 2 more".</summary>
    /// <param name="names">The group names.</param>
    public static string JoinNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count switch
        {
            1 => names[0],
            2 => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_TwoFormat"), names[0], names[1]),
            _ => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Manifest_AndMoreFormat"), names[0], names[1], names.Count - 2),
        };
    }
}
