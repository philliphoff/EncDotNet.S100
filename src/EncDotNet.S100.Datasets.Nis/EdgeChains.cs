using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// Joins the pieces snap-rounded noding leaves into the longest edges the
/// topology allows.
/// </summary>
/// <remarks>
/// Noding splits a line at every vertex another line shares, so two areas
/// with a common boundary come back as one edge per segment. A node is only
/// needed where a line starts or ends, where more or fewer than two edges
/// meet, or where the lines using the edges on either side change. Every
/// other node is removed and the edges through it joined, the same way for
/// every line that uses them, so shared edges stay shared.
/// </remarks>
internal static class EdgeChains
{
    /// <summary>Joins the edges of the lines into chains between the nodes that must stay.</summary>
    /// <param name="graph">The noded graph.</param>
    /// <param name="lines">For each line, the edges it became, in order.</param>
    /// <param name="nextId">Allocates record ids for the new graph's nodes and edges.</param>
    /// <returns>A new graph and, for each line, its edges in it.</returns>
    public static (S57EdgeGraph Graph, List<List<S57EdgeGraph.Use>> Lines) Merge(
        S57EdgeGraph graph, IReadOnlyList<List<S57EdgeGraph.Use>> lines, Func<uint> nextId)
    {
        // Which lines use each edge, and how many edge ends meet at each node.
        var users = new Dictionary<uint, List<int>>();
        for (int line = 0; line < lines.Count; line++)
        {
            foreach (var use in lines[line])
            {
                if (!users.TryGetValue(use.Edge, out var list)) users[use.Edge] = list = [];
                list.Add(line);
            }
        }
        var signature = users.ToDictionary(kv => kv.Key, kv => string.Join(',', kv.Value.Order()));
        var ends = new Dictionary<uint, List<uint>>();
        foreach (var edge in users.Keys)
        {
            var e = graph.Edges[edge];
            Add(e.Begin, edge);
            Add(e.End, edge);
        }

        // Open lines keep their ends; a closed ring needs a node only where
        // something else meets it, or one node when nothing does.
        var keep = new HashSet<uint>();
        foreach (var line in lines)
        {
            if (line.Count == 0 || IsClosed(graph, line)) continue;
            keep.Add(Start(graph, line[0]));
            keep.Add(End(graph, line[^1]));
        }
        foreach (var (node, incident) in ends)
        {
            if (incident.Count != 2 || incident[0] == incident[1] || signature[incident[0]] != signature[incident[1]])
                keep.Add(node);
        }
        foreach (var line in lines)
        {
            if (line.Count > 0 && IsClosed(graph, line) && !line.Any(u => keep.Contains(End(graph, u))))
                keep.Add(Start(graph, line[0]));
        }

        var merged = new S57EdgeGraph(nextId);
        var byKey = new Dictionary<string, S57EdgeGraph.Use>(StringComparer.Ordinal);
        var result = new List<List<S57EdgeGraph.Use>>(lines.Count);
        foreach (var original in lines)
        {
            var line = IsClosed(graph, original) ? FromKeptNode(graph, original, keep) : original;
            var uses = new List<S57EdgeGraph.Use>();
            List<(int X, int Y)>? chain = null;
            foreach (var use in line)
            {
                var positions = graph.Positions(use.Edge, use.Reverse);
                if (chain is null) chain = positions;
                else chain.AddRange(positions.Skip(1));
                if (keep.Contains(End(graph, use)))
                {
                    uses.Add(Edge(merged, byKey, chain));
                    chain = null;
                }
            }
            if (chain is not null) uses.Add(Edge(merged, byKey, chain));
            result.Add(uses);
        }
        return (merged, result);

        void Add(uint node, uint edge)
        {
            if (!ends.TryGetValue(node, out var list)) ends[node] = list = [];
            list.Add(edge);
        }
    }

    private static bool IsClosed(S57EdgeGraph graph, List<S57EdgeGraph.Use> line)
        => Start(graph, line[0]) == End(graph, line[^1]);

    // A closed ring's edges rotated to begin at a node that stays, so no
    // chain runs through the ring's arbitrary first position.
    private static List<S57EdgeGraph.Use> FromKeptNode(S57EdgeGraph graph, List<S57EdgeGraph.Use> ring, HashSet<uint> keep)
    {
        int first = ring.FindIndex(u => keep.Contains(Start(graph, u)));
        return first <= 0 ? ring : [.. ring.Skip(first), .. ring.Take(first)];
    }

    private static uint Start(S57EdgeGraph graph, S57EdgeGraph.Use use)
        => use.Reverse ? graph.Edges[use.Edge].End : graph.Edges[use.Edge].Begin;

    private static uint End(S57EdgeGraph graph, S57EdgeGraph.Use use)
        => use.Reverse ? graph.Edges[use.Edge].Begin : graph.Edges[use.Edge].End;

    // The edge along the positions, reusing one that runs along them either way.
    private static S57EdgeGraph.Use Edge(S57EdgeGraph graph, Dictionary<string, S57EdgeGraph.Use> byKey, List<(int X, int Y)> positions)
    {
        var key = string.Join(';', positions.Select(p => $"{p.X},{p.Y}"));
        if (byKey.TryGetValue(key, out var use)) return use;
        var id = graph.AddEdge(positions);
        byKey[key] = new S57EdgeGraph.Use(id, false);
        var reversed = new List<(int X, int Y)>(positions);
        reversed.Reverse();
        byKey.TryAdd(string.Join(';', reversed.Select(p => $"{p.X},{p.Y}")), new S57EdgeGraph.Use(id, true));
        return byKey[key];
    }
}
