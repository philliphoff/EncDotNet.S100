using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.McpTools;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The Reference tab's trail (symbol dictionary design note §4.4, R7,
/// #1650): which entry it shows, with back and forward history and a
/// breadcrumb. <see cref="Open(ReferenceKey)"/> shows an entry and asks the
/// shell to switch the right dock to the tab; it never opens a second window.
/// </summary>
/// <remarks>
/// <para>
/// History works as a browser's: opening an entry while back in the trail
/// drops the entries ahead of it. Opening the entry already shown adds
/// nothing. The navigator holds keys only; it doesn't check that an entry
/// exists, so the tab says when the catalogues have no such entry.
/// </para>
/// <para>
/// Keys also have a text form for links from MCP and docs,
/// <c>soundcharts://reference/{spec}/{kind}/{code}</c>, which is
/// <see cref="ReferenceKey"/>'s own text form after the scheme
/// (<see cref="TryParse"/>). Registering the scheme with the operating
/// system is out of scope.
/// </para>
/// </remarks>
internal sealed class ReferenceNavigator
{
    /// <summary>The scheme and host of a reference link.</summary>
    public const string LinkPrefix = "soundcharts://reference/";

    /// <summary>The most entries the history keeps; the oldest go first.</summary>
    public const int MaxHistory = 50;

    /// <summary>The most entries the breadcrumb shows, the current one last.</summary>
    public const int MaxCrumbs = 4;

    private readonly List<ReferenceKey> _history = [];
    private readonly List<ReferenceFeature?> _features = [];
    private int _position = -1;

    /// <summary>
    /// Raised when the entry shown changes, by <see cref="Open(ReferenceKey)"/>,
    /// <see cref="Back"/>, <see cref="Forward"/> or <see cref="GoTo"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised by <see cref="Open(ReferenceKey)"/>, even for the entry already
    /// shown: the shell shows the right dock on the Reference tab.
    /// </summary>
    public event EventHandler? ShowRequested;

    /// <summary>The entry shown, or <see langword="null"/> before any is opened.</summary>
    public ReferenceKey? Current => _position >= 0 ? _history[_position] : null;

    /// <summary>
    /// The picked feature <see cref="Current"/> was opened from (Open in
    /// reference, F1), or <see langword="null"/> (R13b, #1721). Back and
    /// forward keep each entry's own.
    /// </summary>
    public ReferenceFeature? CurrentFeature => _position >= 0 ? _features[_position] : null;

    /// <summary>Whether there is an entry to go back to.</summary>
    public bool CanGoBack => _position > 0;

    /// <summary>Whether there is an entry to go forward to.</summary>
    public bool CanGoForward => _position >= 0 && _position < _history.Count - 1;

    /// <summary>The whole history, oldest first.</summary>
    public IReadOnlyList<ReferenceKey> History => _history;

    /// <summary>The position of <see cref="Current"/> in <see cref="History"/>, or -1.</summary>
    public int Position => _position;

    /// <summary>
    /// The breadcrumb: up to <see cref="MaxCrumbs"/> entries of the history
    /// that lead to the current one, with their positions; the current one last.
    /// </summary>
    public IReadOnlyList<(int Position, ReferenceKey Key)> Breadcrumb
    {
        get
        {
            if (_position < 0)
                return [];
            var first = Math.Max(0, _position - MaxCrumbs + 1);
            return [.. Enumerable.Range(first, _position - first + 1).Select(i => (i, _history[i]))];
        }
    }

    /// <summary>
    /// Shows <paramref name="key"/> in the Reference tab and switches the
    /// right dock to it. A new entry drops any forward history.
    /// </summary>
    /// <param name="key">The entry.</param>
    public void Open(ReferenceKey key) => Open(key, null);

    /// <summary>
    /// Shows <paramref name="key"/> in the Reference tab, opened from
    /// <paramref name="feature"/>, and switches the right dock to it. A new
    /// entry drops any forward history; the entry already shown, opened from
    /// another feature, takes that feature.
    /// </summary>
    /// <param name="key">The entry.</param>
    /// <param name="feature">The picked feature it is opened from, or <see langword="null"/>.</param>
    public void Open(ReferenceKey key, ReferenceFeature? feature)
    {
        ArgumentException.ThrowIfNullOrEmpty(key.Code);
        if (Current is not { } current || !SameEntry(current, key))
        {
            if (_position < _history.Count - 1)
            {
                _history.RemoveRange(_position + 1, _history.Count - _position - 1);
                _features.RemoveRange(_position + 1, _features.Count - _position - 1);
            }

            _history.Add(key);
            _features.Add(feature);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
                _features.RemoveRange(0, _features.Count - MaxHistory);
            }

            _position = _history.Count - 1;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (_features[_position] != feature)
        {
            _features[_position] = feature;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the entry a link names: <c>soundcharts://reference/S-101/FeatureType/LateralBeacon</c>
    /// or the key's text form alone.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>Whether the link named an entry; nothing happens when it didn't.</returns>
    public bool Open(string link)
    {
        if (!TryParse(link, out var key))
            return false;
        Open(key);
        return true;
    }

    /// <summary>Goes back one entry (Alt+Left).</summary>
    /// <returns>Whether there was an entry to go back to.</returns>
    public bool Back() => GoTo(_position - 1);

    /// <summary>Goes forward one entry (Alt+Right).</summary>
    /// <returns>Whether there was an entry to go forward to.</returns>
    public bool Forward() => GoTo(_position + 1);

    /// <summary>Shows the entry at <paramref name="position"/> in the history, keeping the history (a breadcrumb).</summary>
    /// <param name="position">The position in <see cref="History"/>.</param>
    /// <returns>Whether the position was in the history and not the current one.</returns>
    public bool GoTo(int position)
    {
        if (position < 0 || position >= _history.Count || position == _position)
            return false;
        _position = position;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The link to <paramref name="key"/>: <c>soundcharts://reference/{spec}/{kind}/{code}</c>.</summary>
    /// <param name="key">The entry.</param>
    /// <returns>The link.</returns>
    public static string LinkOf(ReferenceKey key) =>
        $"{LinkPrefix}{key.Spec.Name}/{key.Kind}/{Uri.EscapeDataString(key.Code)}";

    /// <summary>
    /// Reads a link, <c>soundcharts://reference/{spec}/{kind}/{code}</c>, or
    /// the key's text form alone (<c>S-101/FeatureType/LateralBeacon</c>).
    /// The kind may be written <c>FeatureType</c> or <c>feature_type</c>.
    /// </summary>
    /// <param name="text">The link or key.</param>
    /// <param name="key">The key.</param>
    /// <returns>Whether the text names a key.</returns>
    public static bool TryParse(string? text, out ReferenceKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase))
            trimmed = Uri.UnescapeDataString(trimmed[LinkPrefix.Length..]);
        return ChartReferenceWire.TryParseKey(trimmed, out key);
    }

    // Codes compare as the index's TryGet does, without regard to case.
    private static bool SameEntry(ReferenceKey a, ReferenceKey b) =>
        a.Kind == b.Kind
        && string.Equals(a.Spec.Name, b.Spec.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
}
