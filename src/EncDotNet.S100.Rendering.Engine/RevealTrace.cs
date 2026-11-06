using System.Diagnostics;
using System.Runtime.CompilerServices;
using S100Diag = EncDotNet.S100.Rendering.Engine.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// The reveal trace (map rendering slice&#160;8): how a tile slot's look
/// changes while a view fills in. Each live frame reports, for every visible
/// slot of every tiled owner, the tile it drew there (or none). A change at a
/// slot that showed nothing yet is a <i>first</i> reveal; a change at a slot
/// that already showed something from the same view (another layer's tile
/// landing on top, or a chart tile replacing its stand-in) is an
/// <i>incremental</i> reveal: the slot changes in another step, tagged
/// <c>flash</c> when it comes within 150&#160;ms of the slot's last change
/// (pieces landing one after another) and <c>refine</c> when later (the next
/// wave of cells, such as harbour cells replacing a coarser one). Changes in
/// the same frame (within <see cref="FrameTicks"/>) count once. Counted only
/// while <see cref="S100VectorTileRenderer.SettleTrackingEnabled"/>, so a
/// viewer without the perf harness does no extra work.
/// </summary>
internal static class RevealTrace
{
    private static readonly long FrameTicks = Stopwatch.Frequency * 8 / 1000;
    private static readonly long ViewTicks = Stopwatch.Frequency / 2;
    private static readonly long FlashTicks = Stopwatch.Frequency * 150 / 1000;
    private static readonly Lock Sync = new();
    private static readonly Dictionary<(int Band, int X, int Y), Slot> Slots = new();

    /// <summary>
    /// Reports that <paramref name="owner"/> drew <paramref name="shown"/> (or
    /// nothing) at <paramref name="slot"/> this frame.
    /// </summary>
    public static void Report(object owner, (int Band, int X, int Y) slot, TileKey? shown)
    {
        var now = Stopwatch.GetTimestamp();
        var ownerId = RuntimeHelpers.GetHashCode(owner);
        lock (Sync)
        {
            if (!Slots.TryGetValue(slot, out var state) || now - state.LastSeen > ViewTicks)
            {
                // A slot not seen for a while belongs to another view.
                state = new Slot();
                Slots[slot] = state;
                if (Slots.Count > 16384)
                {
                    Slots.Clear();
                    Slots[slot] = state;
                }
            }

            state.LastSeen = now;
            var had = state.Shown.TryGetValue(ownerId, out var previous);
            if (shown is null || (had && previous == shown.Value))
            {
                return;
            }

            state.Shown[ownerId] = shown.Value;
            if (now - state.LastChange <= FrameTicks)
            {
                return;
            }

            var first = !state.AnyShown;
            var gap = now - state.LastChange;
            state.AnyShown = true;
            state.LastChange = now;
            if (first)
            {
                S100Diag.Telemetry.RevealFirst.Add(1);
            }
            else
            {
                // Pieces landing in quick succession read as flashing; a
                // change long after the last reads as the next refinement.
                S100Diag.Telemetry.RevealIncremental.Add(1, new KeyValuePair<string, object?>(
                    "s100.render.reveal.gap", gap <= FlashTicks ? "flash" : "refine"));
            }
            if (first && S100VectorTileRenderer.LastViewChangeTicks is var viewChanged and > 0)
            {
                S100Diag.Telemetry.RevealFirstDelay.Record(Stopwatch.GetElapsedTime(viewChanged, now).TotalMilliseconds);
            }
        }
    }

    private sealed class Slot
    {
        public readonly Dictionary<int, TileKey> Shown = new();
        public long LastSeen;
        public long LastChange;
        public bool AnyShown;
    }
}
