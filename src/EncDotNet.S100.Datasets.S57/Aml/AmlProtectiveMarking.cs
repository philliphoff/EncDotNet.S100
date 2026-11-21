using System.Text;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The protective markings of NATO Additional Military Layers (AML) data and
/// the "highest wins" rule that picks the one to show for several cells.
/// </summary>
/// <remarks>
/// <para>
/// A marking is a handling rule, not a fault. It's shown exactly as the cell
/// gives it, upper case and never shortened. The known markings rank
/// <see cref="Unclassified"/> &lt; <see cref="Restricted"/> &lt;
/// <see cref="NatoRestricted"/>; any other text ranks above all three, because
/// a marking SoundCharts doesn't know can't be assumed to be lower.
/// </para>
/// <para>
/// The order is provisional: the public samples only use UNCLASSIFIED, and the
/// AML Implementation Guidance Document hasn't been checked for the full set of
/// strings producers write.
/// </para>
/// </remarks>
public static class AmlProtectiveMarking
{
    /// <summary>The lowest marking, <c>UNCLASSIFIED</c>.</summary>
    public const string Unclassified = "UNCLASSIFIED";

    /// <summary>The <c>RESTRICTED</c> marking.</summary>
    public const string Restricted = "RESTRICTED";

    /// <summary>The <c>NATO RESTRICTED</c> marking.</summary>
    public const string NatoRestricted = "NATO RESTRICTED";

    /// <summary>The rank of a marking that isn't one of the known three: above all of them.</summary>
    public const int UnknownRank = 3;

    /// <summary>
    /// Normalizes a marking for display and comparison: trimmed, upper case,
    /// with runs of white space collapsed to one space.
    /// </summary>
    /// <param name="marking">The marking as the data gives it.</param>
    /// <returns>The normalized marking, or <see langword="null"/> when <paramref name="marking"/> is empty.</returns>
    public static string? Normalize(string? marking)
    {
        if (string.IsNullOrWhiteSpace(marking))
            return null;

        var builder = new StringBuilder(marking.Length);
        var space = false;
        foreach (var c in marking.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The rank of a marking: 0 for <see cref="Unclassified"/>, 1 for
    /// <see cref="Restricted"/>, 2 for <see cref="NatoRestricted"/> and
    /// <see cref="UnknownRank"/> for any other text.
    /// </summary>
    /// <param name="marking">The marking.</param>
    /// <returns>The rank; higher is more protected.</returns>
    public static int Rank(string marking)
    {
        ArgumentNullException.ThrowIfNull(marking);
        return Normalize(marking) switch
        {
            Unclassified => 0,
            Restricted => 1,
            NatoRestricted => 2,
            _ => UnknownRank,
        };
    }

    /// <summary>Whether a marking ranks above <see cref="Unclassified"/>.</summary>
    /// <param name="marking">The marking.</param>
    public static bool IsAboveUnclassified(string marking) => Rank(marking) > 0;

    /// <summary>
    /// The marking an AML cell name's classification letter stands for:
    /// <c>U</c> is <see cref="Unclassified"/> and <c>R</c> is
    /// <see cref="Restricted"/>.
    /// </summary>
    /// <param name="letter">The letter from the cell name.</param>
    /// <returns>The marking, or <see langword="null"/> for any other letter.</returns>
    public static string? FromCellNameLetter(char letter) => char.ToUpperInvariant(letter) switch
    {
        'U' => Unclassified,
        'R' => Restricted,
        _ => null,
    };

    /// <summary>
    /// Picks the highest of several markings (decision 11: highest wins).
    /// </summary>
    /// <param name="markings">The markings; empty and <see langword="null"/> entries are skipped.</param>
    /// <returns>
    /// The highest marking and how many other distinct markings tie with it,
    /// or <see langword="null"/> when there is none.
    /// </returns>
    public static AmlMarkingSummary? Highest(IEnumerable<string?> markings)
    {
        ArgumentNullException.ThrowIfNull(markings);

        string? highest = null;
        var highestRank = -1;
        var tied = new List<string>();
        foreach (var raw in markings)
        {
            var marking = Normalize(raw);
            if (marking is null)
                continue;

            var rank = Rank(marking);
            if (rank > highestRank)
            {
                highest = marking;
                highestRank = rank;
                tied.Clear();
            }
            else if (rank == highestRank
                && !string.Equals(marking, highest, StringComparison.Ordinal)
                && !tied.Contains(marking, StringComparer.Ordinal))
            {
                tied.Add(marking);
            }
        }

        return highest is null ? null : new AmlMarkingSummary(highest, tied.Count);
    }
}

/// <summary>
/// The marking shown for several AML cells: the highest one, and how many
/// other distinct markings tie with it at the top.
/// </summary>
/// <param name="Highest">The highest marking, normalized; the first one seen when several tie.</param>
/// <param name="OthersAtTop">How many other distinct markings rank the same as <paramref name="Highest"/>.</param>
public sealed record AmlMarkingSummary(string Highest, int OthersAtTop)
{
    /// <summary>The text to show: the marking, plus "+N" when others tie with it.</summary>
    public string DisplayText => OthersAtTop > 0 ? $"{Highest} +{OthersAtTop}" : Highest;

    /// <summary>Whether the highest marking ranks above <see cref="AmlProtectiveMarking.Unclassified"/>.</summary>
    public bool IsAboveUnclassified => AmlProtectiveMarking.IsAboveUnclassified(Highest);
}
