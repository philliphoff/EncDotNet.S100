using System.Globalization;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// How dense AML points cluster (AML design handoff decision 20): points of
/// the classes that share a clustering, closer than <see cref="RadiusPixels"/>
/// on screen, draw as one counted bubble with a label such as
/// "38 contacts · 2 mine-like". The bubble expands into its points on zoom.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why precomputed.</b> Clustering is in screen space, but an AML cell is
/// drawn once (<see cref="AmlPortrayal.Draw"/>) and its instructions serve
/// every scale: tiles render the same instructions at many scales, and no
/// renderer has a point-cluster facility. So the portrayal clusters the points
/// at each scale of a ladder (<see cref="Scales"/>), fine to coarse, and
/// gives every instruction a scale window instead: a point draws only at
/// scales where it is on its own, and a bubble only in its own rung of the
/// ladder. Scale windows are ordinary <c>ScaleMinimum</c> and
/// <c>ScaleMaximum</c> limits, which every renderer and the tile scale classes
/// already honour, so nothing renderer-side changes.
/// </para>
/// <para>
/// <b>The rule.</b> A rung's radius is <see cref="RadiusPixels"/> nominal S-100
/// pixels (<see cref="AmlSignature.PixelMm"/>) at the rung's scale, on the
/// ground. Each rung merges the clusters of the rung below whose centres are
/// within that radius, biggest first, until no two are, so clusters only ever
/// grow on the way out and a bubble always splits into the bubbles and points
/// of the rung below. A rung covers the scales from the rung below's
/// denominator to its own, so clusters on screen are at least the radius
/// apart throughout it. A point whose <c>SCAMIN</c> hides it in a rung leaves
/// its cluster there.
/// </para>
/// <para>
/// <b>The bubble</b> is anchored at the member nearest the cluster's centre,
/// so picking a bubble picks that member.
/// </para>
/// </remarks>
public sealed record AmlClustering
{
    /// <summary>Creates a clustering.</summary>
    /// <param name="name">Its name, such as "SBO contacts".</param>
    /// <param name="family">The family its bubble draws in; every class that uses it must be of this family.</param>
    /// <param name="bubble">The bubble's glyph, unframed, drawn at the cluster.</param>
    /// <param name="label">The text beside a bubble, from its members, such as "38 contacts · 2 mine-like".</param>
    public AmlClustering(string name, AmlFamily family, AmlGlyph bubble, Func<IReadOnlyList<AmlFeature>, string> label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(bubble);
        ArgumentNullException.ThrowIfNull(label);
        if (bubble.Framed)
            throw new ArgumentException("A cluster bubble is an unframed glyph.", nameof(bubble));
        Name = name;
        Family = family;
        Bubble = bubble;
        Label = label;
    }

    /// <summary>The default radius: points within 24 px cluster.</summary>
    public const double DefaultRadiusPixels = 24;

    /// <summary>The clustering's name.</summary>
    public string Name { get; }

    /// <summary>The family the bubble draws in.</summary>
    public AmlFamily Family { get; }

    /// <summary>The bubble's glyph.</summary>
    public AmlGlyph Bubble { get; }

    /// <summary>The text beside a bubble.</summary>
    public Func<IReadOnlyList<AmlFeature>, string> Label { get; }

    /// <summary>The distance on screen within which points cluster, in nominal S-100 pixels.</summary>
    public double RadiusPixels { get; init; } = DefaultRadiusPixels;

    /// <summary>
    /// The scale denominators of the ladder, ascending: 1:1,000 doubling to
    /// 1:4,096,000 by default. Points never cluster at or above the first;
    /// the last rung's bubbles draw at every smaller scale.
    /// </summary>
    public IReadOnlyList<double> Scales { get; init; } = [.. Enumerable.Range(0, 13).Select(k => 1_000.0 * (1 << k))];

    /// <summary>The bubble's count: its size, in bold, centred on it.</summary>
    public TextInstructionStyle CountStyle { get; init; } = new()
    {
        FontSize = 9,
        FontWeight = TextFontWeight.Bold,
    };

    /// <summary>How far the label sits to the right of the bubble's centre, in millimetres.</summary>
    public double LabelOffset { get; init; } = 3.6;

    /// <summary>The ground radius of a rung at <paramref name="scaleDenominator"/>, in metres.</summary>
    /// <param name="scaleDenominator">The rung's scale denominator.</param>
    public double GroundRadiusMetres(double scaleDenominator) => RadiusPixels * AmlSignature.PixelMm / 1000 * scaleDenominator;

    internal void Validate()
    {
        Bubble.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RadiusPixels);
        if (Scales.Count < 2)
            throw new ArgumentException($"AML clustering '{Name}' needs at least two scales.");
        for (int i = 1; i < Scales.Count; i++)
        {
            if (!(Scales[i] > Scales[i - 1]))
                throw new ArgumentException($"AML clustering '{Name}' scales must ascend.");
        }
    }
}

/// <summary>
/// The scale ladder of one <see cref="AmlClustering"/> over a cell's points:
/// for each point, the largest scale denominator it still draws on its own,
/// and for each rung, its bubbles.
/// </summary>
internal sealed class AmlClusterLadder
{
    private const double MetresPerDegree = 111_320;

    private AmlClusterLadder(IReadOnlyDictionary<AmlFeature, double> alone, IReadOnlyList<AmlCluster> bubbles)
    {
        AloneUntil = alone;
        Bubbles = bubbles;
    }

    /// <summary>The members that cluster somewhere, with the largest scale denominator they draw at on their own.</summary>
    public IReadOnlyDictionary<AmlFeature, double> AloneUntil { get; }

    /// <summary>Every bubble, with its scale window, finest rung first.</summary>
    public IReadOnlyList<AmlCluster> Bubbles { get; }

    /// <summary>Clusters <paramref name="points"/> on <paramref name="clustering"/>'s ladder.</summary>
    /// <param name="clustering">The rule.</param>
    /// <param name="points">The points, each a point feature with its <c>SCAMIN</c> (or <see langword="null"/>), in drawing order.</param>
    public static AmlClusterLadder Build(AmlClustering clustering, IReadOnlyList<(AmlFeature Feature, double? ScaleMinimum)> points)
    {
        var scales = clustering.Scales;
        var alone = new Dictionary<AmlFeature, double>();
        var bubbles = new List<AmlCluster>();
        if (points.Count < 2)
            return new AmlClusterLadder(alone, bubbles);

        // Local metres on the cell's mean latitude: a cell spans a few degrees at most.
        double meanLatitude = points.Average(p => p.Feature.Points[0].Latitude);
        double xScale = MetresPerDegree * Math.Cos(meanLatitude * Math.PI / 180);
        var members = points.Select((p, i) => new Member(
            i,
            p.Feature,
            p.ScaleMinimum ?? double.PositiveInfinity,
            p.Feature.Points[0].Longitude * xScale,
            p.Feature.Points[0].Latitude * MetresPerDegree)).ToList();

        // The first rung: every point alone.
        var clusters = members.Select(m => new List<Member> { m }).ToList();
        List<AmlCluster> open = [];
        for (int k = 1; k < scales.Count; k++)
        {
            double below = scales[k - 1], scale = scales[k];

            // Points hidden throughout this rung by their SCAMIN leave their clusters.
            clusters = [.. clusters.Select(c => c.Where(m => m.ScaleMinimum > below).ToList()).Where(c => c.Count > 0)];
            clusters = Merge(clusters, clustering.GroundRadiusMetres(scale));

            List<AmlCluster> next = [];
            foreach (var cluster in clusters.Where(c => c.Count > 1))
            {
                foreach (var m in cluster)
                    alone.TryAdd(m.Feature, below);

                // The same cluster as the rung below extends its window rather than adding a bubble.
                double top = Math.Min(scale, cluster.Max(m => m.ScaleMinimum));
                var previous = open.FirstOrDefault(b => b.SameMembers(cluster));
                if (previous is not null && previous.ScaleMinimum == below)
                {
                    previous.ScaleMinimum = top;
                    next.Add(previous);
                    continue;
                }

                var bubble = new AmlCluster(cluster, Anchor(cluster), below, top);
                bubbles.Add(bubble);
                next.Add(bubble);
            }

            open = next;
        }

        // The last rung's bubbles draw at every smaller scale their members do.
        foreach (var bubble in open)
            bubble.ScaleMinimum = bubble.MaxMemberScaleMinimum;

        return new AmlClusterLadder(alone, bubbles);
    }

    // Merges clusters whose centres are within radius, biggest first, until none are.
    private static List<List<Member>> Merge(List<List<Member>> clusters, double radius)
    {
        bool merged = true;
        while (merged && clusters.Count > 1)
        {
            merged = false;
            var centres = clusters.Select(Centre).ToList();
            var grid = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < clusters.Count; i++)
            {
                var cell = Cell(centres[i], radius);
                if (!grid.TryGetValue(cell, out var list))
                    grid[cell] = list = [];
                list.Add(i);
            }

            var order = Enumerable.Range(0, clusters.Count)
                .OrderByDescending(i => clusters[i].Count)
                .ThenBy(i => clusters[i].Min(m => m.Index))
                .ToList();
            var taken = new bool[clusters.Count];
            var result = new List<List<Member>>();
            foreach (int i in order)
            {
                if (taken[i])
                    continue;
                taken[i] = true;
                var cluster = new List<Member>(clusters[i]);
                var (cx, cy) = Cell(centres[i], radius);
                for (long gx = cx - 1; gx <= cx + 1; gx++)
                {
                    for (long gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        if (!grid.TryGetValue((gx, gy), out var near))
                            continue;
                        foreach (int j in near)
                        {
                            if (taken[j] || Distance(centres[i], centres[j]) > radius)
                                continue;
                            taken[j] = true;
                            cluster.AddRange(clusters[j]);
                            merged = true;
                        }
                    }
                }

                cluster.Sort((a, b) => a.Index.CompareTo(b.Index));
                result.Add(cluster);
            }

            clusters = result;
        }

        return clusters;
    }

    private static (double X, double Y) Centre(List<Member> cluster) => (cluster.Average(m => m.X), cluster.Average(m => m.Y));

    private static (long, long) Cell((double X, double Y) p, double size) => ((long)Math.Floor(p.X / size), (long)Math.Floor(p.Y / size));

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static AmlFeature Anchor(List<Member> cluster)
    {
        var centre = Centre(cluster);
        return cluster.MinBy(m => Distance((m.X, m.Y), centre))!.Feature;
    }

    internal sealed record Member(int Index, AmlFeature Feature, double ScaleMinimum, double X, double Y);
}

/// <summary>One bubble: its members, the feature it's anchored at, and its scale window.</summary>
internal sealed class AmlCluster
{
    private readonly IReadOnlyList<AmlClusterLadder.Member> _members;

    public AmlCluster(IReadOnlyList<AmlClusterLadder.Member> members, AmlFeature anchor, double scaleMaximum, double scaleMinimum)
    {
        _members = [.. members];
        Members = [.. members.Select(m => m.Feature)];
        Anchor = anchor;
        ScaleMaximum = scaleMaximum;
        ScaleMinimum = scaleMinimum;
    }

    /// <summary>The members, in drawing order.</summary>
    public IReadOnlyList<AmlFeature> Members { get; }

    /// <summary>The member the bubble is drawn at.</summary>
    public AmlFeature Anchor { get; }

    /// <summary>The smallest scale denominator the bubble draws at.</summary>
    public double ScaleMaximum { get; }

    /// <summary>The largest scale denominator the bubble draws at; infinite for every smaller scale.</summary>
    public double ScaleMinimum { get; set; }

    /// <summary>The largest <c>SCAMIN</c> of the members; infinite when one has none.</summary>
    public double MaxMemberScaleMinimum => _members.Max(m => m.ScaleMinimum);

    public bool SameMembers(List<AmlClusterLadder.Member> cluster) =>
        cluster.Count == _members.Count && cluster.Select(m => m.Index).SequenceEqual(_members.Select(m => m.Index));

    /// <summary>The bubble's instructions: the bubble, its count, and its label to the right.</summary>
    public IEnumerable<DrawingInstruction> Build(AmlClustering clustering, AmlDrawingContext context)
    {
        var builder = context.At(AmlDrawingContext.PointPriority + 5) with
        {
            ScaleMaximum = ScaleMaximum,
            ScaleMinimum = double.IsPositiveInfinity(ScaleMinimum) ? null : ScaleMinimum,
        };
        var id = Anchor.Id;
        yield return builder.Point(id, AmlSignature.FramedSymbol(clustering.Family, clustering.Bubble));
        yield return builder.Text(
            id,
            Members.Count.ToString(CultureInfo.InvariantCulture),
            clustering.CountStyle with { FontColor = clustering.Family.ColorToken });
        yield return builder.Text(
            id,
            clustering.Label(Members),
            context.Label with { HorizontalAlignment = TextHorizontalAlignment.Start, OffsetX = clustering.LabelOffset, VerticalAlignment = TextVerticalAlignment.Top, OffsetY = 1.2 });
    }
}
