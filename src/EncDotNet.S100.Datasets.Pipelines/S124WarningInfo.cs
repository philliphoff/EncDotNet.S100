using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Projects a typed S-124 warning part into the product-neutral
/// <see cref="NavigationalWarningInfo"/> that object-information UIs show.
/// </summary>
internal static partial class S124WarningInfo
{
    /// <summary>The <c>referenceCategory</c> of a warning cancellation.</summary>
    private const int CancellationCategory = 1;

    /// <summary>The <c>referenceCategory</c> of an in-force bulletin's list (Edition 2.0.0 Table 8-1).</summary>
    private const int InForceCategory = 3;

    /// <summary>
    /// Builds the warning as a whole, from its preamble and references alone:
    /// what a <see cref="NavigationalWarningSet"/> needs from a dataset that
    /// may have no parts, such as a cancellation or an in-force bulletin.
    /// </summary>
    /// <param name="warning">The typed warning.</param>
    /// <returns>The warning, with no validity, text or geometry.</returns>
    public static NavigationalWarningInfo Message(S124NavigationalWarning warning)
    {
        var preamble = warning.Preamble;
        var msi = preamble?.MessageSeriesIdentifier;
        return new NavigationalWarningInfo
        {
            Series = Trimmed(msi?.NameOfSeries),
            Number = FormatNumber(msi?.WarningNumber, msi?.Year),
            GeneralType = preamble?.NavwarnTypeGeneralName,
            Title = Trimmed(preamble?.Title),
            GeneralArea = Trimmed(preamble?.GeneralArea),
            Locality = Trimmed(preamble?.Locality),
            Issuer = Trimmed(preamble?.PromulgatingAuthority) ?? Trimmed(msi?.ProductionAgency),
            PublicationTime = preamble?.PublicationTime,
            CancellationDate = preamble?.CancellationDate,
            Reference = ReferenceOf(msi),
            Cancels = [.. Cancellations(warning)],
            InForceBulletin = Bulletin(warning),
        };
    }

    /// <summary>
    /// Builds the message for <paramref name="part"/>, reading identification,
    /// general type, area and issuer from the warning's preamble.
    /// </summary>
    /// <param name="warning">The typed warning that owns the part.</param>
    /// <param name="part">The part that was picked.</param>
    /// <returns>The warning message for the part.</returns>
    public static NavigationalWarningInfo From(S124NavigationalWarning warning, S124NavwarnPart part)
    {
        var preamble = warning.Preamble;
        var msi = preamble?.MessageSeriesIdentifier;

        return new NavigationalWarningInfo
        {
            Series = Trimmed(msi?.NameOfSeries),
            Number = FormatNumber(msi?.WarningNumber, msi?.Year),
            WarningType = part.NavwarnType,
            GeneralType = preamble?.NavwarnTypeGeneralName,
            Title = Trimmed(preamble?.Title),
            GeneralArea = Trimmed(preamble?.GeneralArea),
            Locality = Trimmed(preamble?.Locality),
            Issuer = Trimmed(preamble?.PromulgatingAuthority) ?? Trimmed(msi?.ProductionAgency),
            PublicationTime = preamble?.PublicationTime,
            CancellationDate = preamble?.CancellationDate,
            Validity = [.. part.ValidityRanges.Select(r => new NavigationalWarningValidity(r.Start, r.End))],
            Texts = [.. part.Information.Select(i => new NavigationalWarningText(i.Language, i.Text))],
            HasLocation = part.GeometryKind != S124GeometryKind.None || part.AffectedAreas.Count > 0,
            Reference = ReferenceOf(msi),
            Cancels = [.. Cancellations(warning)],
            InForceBulletin = Bulletin(warning),
            AffectedAreas = [.. part.AffectedAreas
                .Select(a => Geometry(a.GeometryKind, a.Coordinates))
                .OfType<S100FeatureGeometry>()],
        };
    }

    /// <summary>
    /// The warnings <paramref name="warning"/> cancels: each
    /// <c>messageSeriesIdentifier</c> of a cancellation reference (Edition
    /// 2.0.0), else its <c>messageReference</c> text read as
    /// "[series] number/year" (Edition 1.0.0, e.g. <c>"NAVAREA I 0412/2026"</c>).
    /// </summary>
    internal static IEnumerable<NavigationalWarningReference> Cancellations(S124NavigationalWarning warning)
    {
        foreach (var reference in warning.References)
        {
            if (reference.ReferenceCategory != CancellationCategory)
                continue;
            var any = false;
            foreach (var msi in reference.Warnings)
            {
                if (ReferenceOf(msi) is { } cancelled)
                {
                    any = true;
                    yield return cancelled;
                }
            }

            if (!any && ParseMessageReference(reference.MessageReference) is { } parsed)
                yield return parsed;
        }
    }

    /// <summary>
    /// What <paramref name="warning"/> lists as in force when it is an
    /// in-force bulletin, or <c>null</c>. A bulletin is recognised by a
    /// <c>References</c> with <c>referenceCategory</c> 3 (In-Force) that lists
    /// <c>messageSeriesIdentifier</c>s or sets <c>noMessageOnHand</c>
    /// (Edition 2.0.0 Table 8-1, §8.1.3, §8.1.4). Its <c>navwarnTypeGeneral</c>
    /// 8 and its own <c>warningType</c> 9–12 (NAVAREA, sub-area, coastal and
    /// local in-force bulletin) are supporting signals, not the trigger; the
    /// series comes from its own <c>messageSeriesIdentifier</c>.
    /// </summary>
    internal static NavigationalWarningBulletin? Bulletin(S124NavigationalWarning warning)
    {
        var lists = warning.References.Where(r => r.ReferenceCategory == InForceCategory).ToList();
        if (lists.Count == 0)
            return null;
        var inForce = lists.SelectMany(r => r.Warnings).Select(ReferenceOf).OfType<NavigationalWarningReference>().ToList();
        var noMessage = lists.Any(r => r.NoMessageOnHand == true);
        if (inForce.Count == 0 && !noMessage)
            return null;
        return new NavigationalWarningBulletin(inForce, noMessage && inForce.Count == 0);
    }

    /// <summary>A <c>messageSeriesIdentifier</c> as a reference, or <c>null</c> without a warning number.</summary>
    private static NavigationalWarningReference? ReferenceOf(S124MessageSeriesIdentifier? msi) =>
        msi?.WarningNumber is { } number
            ? new NavigationalWarningReference(Trimmed(msi.NameOfSeries), number, msi.Year)
            {
                InteroperabilityIdentifier = Trimmed(msi.InteroperabilityIdentifier),
            }
            : null;

    /// <summary>Reads "[series] number/year" (the year two or four digits), or <c>null</c>.</summary>
    internal static NavigationalWarningReference? ParseMessageReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = MessageReferencePattern().Match(text.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || !int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            return null;
        return new NavigationalWarningReference(Trimmed(match.Groups["series"].Value), number, year);
    }

    /// <summary>A part's or an affected area's geometry, or <c>null</c> when it has none.</summary>
    internal static S100FeatureGeometry? Geometry(S124GeometryKind kind, IReadOnlyList<EncDotNet.S100.DataModel.GeoPosition> coordinates) =>
        coordinates.Count == 0
            ? null
            : kind switch
            {
                S124GeometryKind.Point => new S100FeatureGeometry { Primitive = S100GeometryType.Point, Points = coordinates },
                S124GeometryKind.Curve => new S100FeatureGeometry { Primitive = S100GeometryType.Curve, Curves = [coordinates] },
                S124GeometryKind.Surface => new S100FeatureGeometry { Primitive = S100GeometryType.Surface, ExteriorRing = coordinates },
                _ => null,
            };

    [GeneratedRegex(@"^(?<series>.*?)\s*(?<number>\d+)\s*/\s*(?<year>\d{2}|\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageReferencePattern();

    /// <summary>
    /// The number as mariners write it: three digits, a slash and the
    /// two-digit year (<c>"098/26"</c>); the number alone without a year.
    /// </summary>
    internal static string? FormatNumber(int? number, int? year)
    {
        if (number is not { } n)
            return null;
        var digits = n.ToString("D3", CultureInfo.InvariantCulture);
        return year is { } y
            ? $"{digits}/{(y % 100).ToString("D2", CultureInfo.InvariantCulture)}"
            : digits;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
