using System.Globalization;
using EncDotNet.S100.Datasets.S124.DataModel;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Projects a typed S-124 warning part into the product-neutral
/// <see cref="NavigationalWarningInfo"/> that object-information UIs show.
/// </summary>
internal static class S124WarningInfo
{
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
        };
    }

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
