using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// What an AML cell's file name says about it: the producer, the product, the
/// scale band and the classification letter.
/// </summary>
/// <remarks>
/// <para>
/// AML 1.0 and 2.1 names are eight characters,
/// <c>{producer:2}{product:1}{band:1}{classification:1}{id:3}</c>, for
/// example <c>UKC6U114</c>: a UK Contour Line Bathymetry cell in scale band 6,
/// unclassified. The product letters are C (CLB), E (ESB), L (LBO), M (MFF),
/// R (RAL) and S (SBO).
/// </para>
/// <para>
/// The AML 3.0 trial cells are named
/// <c>AML{producer:3}{band:1}{classification:1}_…</c>, for example
/// <c>AMLDEU7U_NKKK4824_CLB_SB7_TrialData</c>. A product token is the first
/// later part, split on <c>_</c> or <c>-</c>, of three capital letters
/// (<c>CLB</c> here). Not every 3.0 name has one.
/// </para>
/// <para>
/// The classification letter is the only marking the AML 3.0 trial cells
/// carry: their <c>DSID</c> <c>COMT</c> is empty.
/// </para>
/// </remarks>
public sealed partial record AmlCellName
{
    /// <summary>The producer code: two letters for 1.0/2.1 (<c>UK</c>), three for 3.0 (<c>DEU</c>).</summary>
    public required string Producer { get; init; }

    /// <summary>
    /// The product code the name gives (<c>CLB</c>, <c>SBO</c>, …), or
    /// <see langword="null"/> for a 3.0 name without a product token.
    /// </summary>
    public string? Product { get; init; }

    /// <summary>The scale band, 0–9.</summary>
    public required int ScaleBand { get; init; }

    /// <summary>The classification letter, upper case (<c>U</c>, <c>R</c>, …).</summary>
    public required char ClassificationLetter { get; init; }

    /// <summary>Whether the name follows the AML 3.0 pattern.</summary>
    public required bool IsAml30Pattern { get; init; }

    /// <summary>
    /// The marking the classification letter stands for (see
    /// <see cref="AmlProtectiveMarking.FromCellNameLetter"/>), or
    /// <see langword="null"/> for a letter that isn't U or R.
    /// </summary>
    public string? Marking => AmlProtectiveMarking.FromCellNameLetter(ClassificationLetter);

    [GeneratedRegex(@"^(?<producer>[A-Z]{2})(?<product>[CELMRS])(?<band>[0-9])(?<class>[A-Z])[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Aml1Pattern();

    [GeneratedRegex(@"^AML(?<producer>[A-Z]{3})(?<band>[0-9])(?<class>[A-Z])(?:[_-](?<rest>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Aml3Pattern();

    [GeneratedRegex(@"^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductToken();

    /// <summary>
    /// Parses an AML cell name.
    /// </summary>
    /// <param name="name">The cell's file name or data set name, with or without its extension or folder.</param>
    /// <param name="cellName">The parsed name, when the name follows an AML pattern.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> follows an AML pattern.</returns>
    public static bool TryParse(string? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AmlCellName? cellName)
    {
        cellName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var stem = Path.GetFileNameWithoutExtension(name.Trim());

        var match = Aml3Pattern().Match(stem);
        if (match.Success)
        {
            string? product = null;
            if (match.Groups["rest"].Success)
            {
                foreach (var part in match.Groups["rest"].Value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ProductToken().IsMatch(part))
                    {
                        product = part;
                        break;
                    }
                }
            }

            cellName = new AmlCellName
            {
                Producer = match.Groups["producer"].Value,
                Product = product,
                ScaleBand = Digit(match.Groups["band"].Value),
                ClassificationLetter = match.Groups["class"].Value[0],
                IsAml30Pattern = true,
            };
            return true;
        }

        match = Aml1Pattern().Match(stem);
        if (match.Success)
        {
            cellName = new AmlCellName
            {
                Producer = match.Groups["producer"].Value,
                Product = ProductFromLetter(match.Groups["product"].Value[0]),
                ScaleBand = Digit(match.Groups["band"].Value),
                ClassificationLetter = match.Groups["class"].Value[0],
                IsAml30Pattern = false,
            };
            return true;
        }

        return false;
    }

    private static int Digit(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string? ProductFromLetter(char letter) => letter switch
    {
        'C' => "CLB",
        'E' => "ESB",
        'L' => "LBO",
        'M' => "MFF",
        'R' => "RAL",
        'S' => "SBO",
        _ => null,
    };
}
