using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Formats a picked feature's attribute rows for Object Information (#1401):
/// typed values (times, time ranges, depths) in the user's time format and
/// depth unit, and the rows the panel shows elsewhere (the not-for-navigation
/// notice, long text, a warning's text) taken out of the table.
/// </summary>
internal static class PickAttributeFormatter
{
    /// <summary>
    /// Formats the typed values of <paramref name="source"/>, at any depth, in
    /// <paramref name="timeFormat"/> and <paramref name="depthUnit"/>. Rows
    /// that aren't typed are kept as they are.
    /// </summary>
    /// <param name="source">The attribute rows.</param>
    /// <param name="timeFormat">The time format.</param>
    /// <param name="depthUnit">The depth unit.</param>
    /// <returns>The formatted rows.</returns>
    public static IReadOnlyList<PickAttribute> Format(IReadOnlyList<PickAttribute> source, TimeFormat timeFormat, DepthUnit depthUnit)
    {
        ArgumentNullException.ThrowIfNull(source);
        var list = new List<PickAttribute>(source.Count);
        foreach (var attr in source)
            list.Add(FormatOne(attr, timeFormat, depthUnit));
        return list;
    }

    private static PickAttribute FormatOne(PickAttribute attr, TimeFormat timeFormat, DepthUnit depthUnit)
    {
        var children = attr.Children.Count == 0
            ? attr.Children
            : Format(attr.Children, timeFormat, depthUnit);

        if (attr.DateTimeValue is { } dt)
            return With(attr, TimeFormatting.Format(dt, timeFormat), children);

        if (attr.DateTimeRangeValue is { } range)
            return With(attr, TimeFormatting.FormatTimeRange(range.Start, range.End, timeFormat), children);

        if (attr.DepthMetresValue is { } metres)
            return With(attr, DepthFormatting.Format(metres, depthUnit), children);

        if (attr.Children.Count != 0 && !ReferenceEquals(children, attr.Children))
            return With(attr, attr.DisplayValue, children);

        return attr;
    }

    /// <summary>
    /// Drops the not-for-navigation notice rows
    /// (<see cref="PickAttribute.IsNavigationNotice"/>) that
    /// <see cref="PickReportViewModel.NavigationNotice"/> already shows.
    /// </summary>
    /// <param name="rows">The attribute rows.</param>
    /// <returns>The rows without the notice.</returns>
    public static IReadOnlyList<PickAttribute> WithoutNavigationNotice(IReadOnlyList<PickAttribute> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Any(r => r.IsNavigationNotice)
            ? rows.Where(r => !r.IsNavigationNotice).ToList()
            : rows;
    }

    /// <summary>
    /// Drops the long text values (see
    /// <see cref="PickTextCardViewModel.IsLongValue"/>) from the table rows,
    /// adding each to <paramref name="lifted"/> with its complex parent. A
    /// complex row left with no children is dropped.
    /// </summary>
    /// <param name="rows">The attribute rows.</param>
    /// <param name="lifted">Receives the long values, each with its complex parent or <see langword="null"/>.</param>
    /// <returns>The rows without the long values.</returns>
    public static IReadOnlyList<PickAttribute> WithoutLongValues(
        IReadOnlyList<PickAttribute> rows, List<(PickAttribute Attribute, PickAttribute? Parent)> lifted)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(lifted);

        var result = new List<PickAttribute>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Children.Count == 0)
            {
                if (PickTextCardViewModel.IsLongValue(row))
                    lifted.Add((row, null));
                else
                    result.Add(row);
                continue;
            }

            var kept = new List<PickAttribute>(row.Children.Count);
            foreach (var child in row.Children)
            {
                if (PickTextCardViewModel.IsLongValue(child))
                    lifted.Add((child, row));
                else
                    kept.Add(child);
            }

            if (kept.Count == row.Children.Count)
                result.Add(row);
            else if (kept.Count > 0)
                result.Add(With(row, row.DisplayValue, kept));
        }
        return result;
    }

    /// <summary>
    /// Drops the warning text rows (S-124 <c>warningInformation/information</c>)
    /// that the Warning card shows, keeping the rest of the complex attribute
    /// (its <c>navwarnTypeDetails</c>).
    /// </summary>
    /// <param name="rows">The attribute rows.</param>
    /// <returns>The rows without the warning text.</returns>
    public static IReadOnlyList<PickAttribute> WithoutWarningText(IReadOnlyList<PickAttribute> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        static bool IsText(PickAttribute child) =>
            child.Code.Equals("information", StringComparison.OrdinalIgnoreCase)
            || child.Code.StartsWith("information.", StringComparison.OrdinalIgnoreCase);

        var result = new List<PickAttribute>(rows.Count);
        foreach (var row in rows)
        {
            if (!row.Code.Equals("warningInformation", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(row);
                continue;
            }

            var kept = row.Children.Where(c => !IsText(c)).ToList();
            if (kept.Count == 0)
                continue;
            result.Add(With(row, row.DisplayValue, kept));
        }
        return result;
    }

    private static PickAttribute With(PickAttribute row, string? displayValue, IReadOnlyList<PickAttribute> children) => new()
    {
        Code = row.Code,
        Name = row.Name,
        RawValue = row.RawValue,
        DisplayValue = displayValue,
        DateTimeValue = row.DateTimeValue,
        DateTimeRangeValue = row.DateTimeRangeValue,
        DepthMetresValue = row.DepthMetresValue,
        ExternalText = row.ExternalText,
        IsNavigationNotice = row.IsNavigationNotice,
        IsStaleNotice = row.IsStaleNotice,
        Mark = row.Mark,
        Children = children,
    };
}
