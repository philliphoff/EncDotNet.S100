namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Splits a <c>PortrayalMain</c> run's visit sequence into shards and merges
/// the shards' output back into the order a full run emits it
/// (<see cref="LuaShardOptions"/>).
/// </summary>
/// <remarks>
/// A full S-100 Part 9a run portrays every visited feature except
/// <c>TextPlacement</c> in visit order, emitting once per feature, then emits
/// the <c>TextPlacement</c> features that got text, in visit order. A shard
/// runs the same loop over a subset of the visits in the same relative order,
/// so each of its emits can be matched to one visit, and sorting every
/// shard's emits by (phase, visit position) restores the full run's order.
/// </remarks>
internal static class LuaShardPlan
{
    /// <summary>The <c>Feature.Code</c> that S-100 Part 9a's main loop portrays last.</summary>
    internal const string TextPlacementCode = "TextPlacement";

    /// <summary>
    /// Consecutive groups are dealt to shards in blocks of about this many
    /// visits, so each shard gets a spread of the cell (features of one kind
    /// tend to sit together in a dataset) without cutting groups.
    /// </summary>
    private const int BlockSize = 32;

    /// <summary>
    /// The number of shards a run of <paramref name="visits"/> visits gets:
    /// at most <paramref name="maxShards"/>, with at least
    /// <paramref name="minVisitsPerShard"/> visits each; <c>1</c> means a full run.
    /// </summary>
    public static int ShardCount(int visits, int maxShards, int minVisitsPerShard) =>
        Math.Clamp(visits / Math.Max(1, minVisitsPerShard), 1, Math.Max(1, maxShards));

    /// <summary>
    /// Assigns every visit to a shard, keeping each group in one shard.
    /// </summary>
    /// <returns>The visit positions (0-based, ascending) of each shard.</returns>
    public static List<int>[] Assign(IReadOnlyList<int> groups, int shards)
    {
        var positions = new List<int>[shards];
        for (var i = 0; i < shards; i++)
        {
            positions[i] = new List<int>(groups.Count / shards + BlockSize);
        }

        var shardOfGroup = new Dictionary<int, int>();
        var shard = 0;
        var inBlock = 0;
        for (var p = 0; p < groups.Count; p++)
        {
            if (!shardOfGroup.TryGetValue(groups[p], out var s))
            {
                if (inBlock >= BlockSize)
                {
                    shard = (shard + 1) % shards;
                    inBlock = 0;
                }

                s = shard;
                shardOfGroup[groups[p]] = s;
            }

            positions[s].Add(p);
            if (s == shard)
            {
                inBlock++;
            }
        }

        return positions;
    }

    /// <summary>
    /// Merges the shards' emits into full-run order, or returns
    /// <see langword="null"/> when an emit can't be matched to one of its
    /// shard's visits (the catalogue's main loop isn't the one this plan
    /// assumes), so the caller can fall back to a full run.
    /// </summary>
    /// <param name="refs">The visited features' references, by visit position.</param>
    /// <param name="codes">The visited features' codes, by visit position.</param>
    /// <param name="shardPositions">Each shard's visit positions, ascending.</param>
    /// <param name="shardEmits">Each shard's emits, in emission order.</param>
    public static List<EmittedInstruction>? Merge(
        IReadOnlyList<string> refs,
        IReadOnlyList<string> codes,
        IReadOnlyList<List<int>> shardPositions,
        IReadOnlyList<IReadOnlyList<EmittedInstruction>> shardEmits)
    {
        var total = 0;
        foreach (var emits in shardEmits)
        {
            total += emits.Count;
        }

        // Key: phase in the high bit, visit position below, so one sort
        // restores the full run's order; every key is unique because each
        // visit is matched once.
        var keyed = new List<(long Key, EmittedInstruction Emit)>(total);
        for (var s = 0; s < shardPositions.Count; s++)
        {
            var positions = shardPositions[s];
            var emits = shardEmits[s];
            var main = 0;
            var text = 0;
            var inText = false;
            foreach (var emit in emits)
            {
                var matched = -1;
                if (!inText)
                {
                    matched = Find(positions, ref main, refs, codes, emit.FeatureRef, textPlacement: false);
                    if (matched < 0)
                    {
                        inText = true;
                    }
                }

                var phase = 0L;
                if (inText)
                {
                    matched = Find(positions, ref text, refs, codes, emit.FeatureRef, textPlacement: true);
                    phase = 1L << 40;
                }

                if (matched < 0)
                {
                    return null;
                }

                keyed.Add((phase | (uint)matched, emit));
            }
        }

        keyed.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        var merged = new List<EmittedInstruction>(keyed.Count);
        foreach (var (_, emit) in keyed)
        {
            merged.Add(emit);
        }

        return merged;
    }

    // Finds the next visit at or after `cursor` of the right phase whose
    // feature is `featureRef`, and moves the cursor past it.
    private static int Find(
        List<int> positions, ref int cursor, IReadOnlyList<string> refs, IReadOnlyList<string> codes,
        string featureRef, bool textPlacement)
    {
        for (var i = cursor; i < positions.Count; i++)
        {
            var p = positions[i];
            if (string.Equals(codes[p], TextPlacementCode, StringComparison.Ordinal) != textPlacement)
            {
                continue;
            }

            if (string.Equals(refs[p], featureRef, StringComparison.Ordinal))
            {
                cursor = i + 1;
                return p;
            }
        }

        return -1;
    }
}
