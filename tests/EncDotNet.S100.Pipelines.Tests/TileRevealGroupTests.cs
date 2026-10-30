using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins <see cref="TileRevealGroup"/>'s reveal rule: the tiled layers of one
/// dataset (S-101 area fills and line work) reveal each tile position
/// together on first render, a slow layer cannot hold the others past the
/// timeout, and a position once shown is never hidden again.
/// </summary>
public sealed class TileRevealGroupTests
{
    private const string Areas = "0:s101.areas";
    private const string Lines = "1:s101.linework";
    private static readonly TileKey Key = new(14, 100, 200);

    [Fact]
    public void IsRevealed_SiblingHasNoTile_HoldsUntilItLands()
    {
        var group = new TileRevealGroup();
        var lines = new HashSet<TileKey>();

        // Frame 1: the area fill tile is cached, the line work tile is not.
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], lines.Contains));
        Assert.False(group.IsRevealed(Key));

        // Frame 2: the line work tile has landed; both reveal.
        lines.Add(Key);
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], lines.Contains));
        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_SiblingTileLandsMidFrame_HoldsBothUntilNextFrame()
    {
        var group = new TileRevealGroup();
        var lines = new HashSet<TileKey>();
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], lines.Contains));

        // Frame 2: the areas layer paints first and holds the position.
        group.Report(Areas, [Key], _ => true);
        Assert.False(group.IsRevealed(Key));

        // The line work tile lands before the lines layer paints in the same
        // frame. Revealing it now would show the lines a frame before the fills.
        lines.Add(Key);
        group.Report(Lines, [Key], lines.Contains);
        Assert.False(group.IsRevealed(Key));

        // Frame 3: both reveal together.
        group.Report(Areas, [Key], _ => true);
        Assert.True(group.IsRevealed(Key));
        group.Report(Lines, [Key], lines.Contains);
        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_SiblingDoesNotNeedPosition_RevealsAtOnce()
    {
        // The line work has no content at this position, so nothing waits on it.
        var group = new TileRevealGroup();
        Frame(group, (Areas, [Key], _ => true), (Lines, [], _ => false));

        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_LoneMember_RevealsAtOnce()
    {
        var group = new TileRevealGroup();
        group.Report(Areas, [Key], _ => true);

        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_SiblingStoppedPainting_DoesNotHold()
    {
        // A sibling that is hidden, culled or out of scale stops reporting; it
        // must not hold the others back.
        var group = new TileRevealGroup();
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => false));
        Assert.False(group.IsRevealed(Key));

        group.Report(Areas, [Key], _ => true);
        group.Report(Areas, [Key], _ => true);

        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_SlowSibling_RevealsAfterHoldTimeout()
    {
        var now = 0L;
        var hold = TimeSpan.FromMilliseconds(400);
        var group = new TileRevealGroup(hold, () => now);

        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => false));
        Assert.False(group.IsRevealed(Key));

        now += Ticks(hold) - 1;
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => false));
        Assert.False(group.IsRevealed(Key));

        now += 1;
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => false));
        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_RevealedPositionSiblingEvicted_StaysRevealed()
    {
        // Panning back over a position already shown must not hide the fills
        // while the line work tile, evicted meanwhile, is rasterised again.
        var group = new TileRevealGroup();
        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => true));
        Assert.True(group.IsRevealed(Key));

        Frame(group, (Areas, [Key], _ => true), (Lines, [Key], _ => false));

        Assert.True(group.IsRevealed(Key));
    }

    [Fact]
    public void IsRevealed_SiblingInAnotherScaleClass_MatchesByPosition()
    {
        // Each layer keys a position by its own SCAMIN scale class (issue
        // #774); the hold is by position, so the sibling's own key is checked.
        var group = new TileRevealGroup();
        var linesKey = Key with { ScaleClass = 2 };
        var lines = new HashSet<TileKey>();
        Frame(group, (Areas, [Key], _ => true), (Lines, [linesKey], lines.Contains));
        Assert.False(group.IsRevealed(Key));

        lines.Add(linesKey);
        Frame(group, (Areas, [Key], _ => true), (Lines, [linesKey], lines.Contains));

        Assert.True(group.IsRevealed(Key));
    }

    /// <summary>Reports every member once, in paint order: one frame.</summary>
    private static void Frame(
        TileRevealGroup group,
        params (string Member, TileKey[] Needed, Func<TileKey, bool> HasTile)[] members)
    {
        foreach (var (member, needed, hasTile) in members)
        {
            group.Report(member, needed, hasTile);
        }
    }

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);
}
