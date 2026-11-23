using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// What the Chart reference activity reads from the map (symbol dictionary
/// §4.5, R5, #1638): what a product draws in view, for <b>In view</b>, and
/// hover to find. The app's <see cref="ChartReferenceMap"/> reads the Legend
/// tab's services, so the two agree.
/// </summary>
internal interface IChartReferenceMap
{
    /// <summary>
    /// Raised on the UI thread when what is drawn in view may have changed:
    /// on pan end, never under the pointer, and when the stack is redrawn.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>What <paramref name="spec"/> draws in the settled view.</summary>
    /// <param name="spec">The portrayal spec name (<c>S-101</c>).</param>
    ChartReferenceDrawn DrawnInView(string spec);

    /// <summary>A row is hovered or focused: its features are ringed after the hover delay.</summary>
    /// <param name="target">The row.</param>
    void Hover(ChartReferenceTarget target);

    /// <summary>The pointer or focus left a row: its rings clear.</summary>
    /// <param name="target">The row, or null for whichever row is hovered.</param>
    void Leave(ChartReferenceTarget? target);

    /// <summary>
    /// How many features of <paramref name="target"/> are drawn in the map
    /// view now: the Reference tab's In view count (R7, #1650).
    /// </summary>
    /// <param name="target">The entry.</param>
    int CountInView(ChartReferenceTarget target);

    /// <summary>
    /// Rings <paramref name="target"/>'s features in view now, without the
    /// hover delay: the Reference tab's Find (R7, #1650). They clear on
    /// <see cref="Leave"/> or when the map moves.
    /// </summary>
    /// <param name="target">The entry.</param>
    void Find(ChartReferenceTarget target);
}

/// <summary>
/// The app's <see cref="IChartReferenceMap"/>: In view from
/// <see cref="LegendService.DrawnInView"/>, which follows the view on pan end
/// as the Legend tab does (legends D5), and hover to find through
/// <see cref="LegendFindService"/> (legends D19).
/// </summary>
internal sealed class ChartReferenceMap : IChartReferenceMap, IDisposable
{
    private readonly LegendService _legend;
    private readonly LegendFindService? _find;

    /// <summary>Creates the link.</summary>
    /// <param name="legend">The legend, whose sections follow the settled view.</param>
    /// <param name="find">Hover to find; null in hosts without a map.</param>
    public ChartReferenceMap(LegendService legend, LegendFindService? find)
    {
        ArgumentNullException.ThrowIfNull(legend);
        _legend = legend;
        _find = find;
        _legend.Changed += OnLegendChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public ChartReferenceDrawn DrawnInView(string spec) => _legend.DrawnInView(spec);

    /// <inheritdoc />
    public void Hover(ChartReferenceTarget target) => _find?.HoverReference(target);

    /// <inheritdoc />
    public void Leave(ChartReferenceTarget? target) => _find?.LeaveReference(target);

    /// <inheritdoc />
    public int CountInView(ChartReferenceTarget target) => _legend.FindReferenceFeatures(target, maxRings: 0).MatchCount;

    /// <inheritdoc />
    public void Find(ChartReferenceTarget target) => _find?.FindReferenceNow(target);

    /// <inheritdoc />
    public void Dispose() => _legend.Changed -= OnLegendChanged;

    private void OnLegendChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
