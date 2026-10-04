using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Combines an S-100 HDF5 dataset's <c>issueDate</c> and <c>issueTime</c> root
/// attributes (S-100 Part 10c) into one UTC time. Producers write them in
/// several forms: <c>20210414</c> / <c>2026-10-03</c> for the date, and
/// <c>135227Z</c>, <c>13:52:27</c> or <c>17:36:13.871916</c> for the time, with
/// an optional <c>Z</c> or UTC offset.
/// </summary>
/// <remarks>
/// The issue time is when the producer issued the file, which for a forecast
/// is usually some time after the model run it carries (NOAA's 12:00Z cbofs
/// run is issued around 13:45Z).
/// </remarks>
public static class S100IssueTime
{
    /// <summary>
    /// The UTC issue time, or <see langword="null"/> when the date is missing
    /// or unreadable. A missing or unreadable time is taken as 00:00Z of the date.
    /// </summary>
    /// <remarks>
    /// Both attributes follow <see cref="Iso8601Text"/>, the same grammar the
    /// validation rules check them against; a time without a zone designator
    /// is taken as UTC.
    /// </remarks>
    /// <param name="issueDate">The <c>issueDate</c> attribute.</param>
    /// <param name="issueTime">The <c>issueTime</c> attribute, if any.</param>
    public static DateTime? Parse(string? issueDate, string? issueTime)
    {
        if (!Iso8601Text.TryParseCalendarDate(issueDate, out var date))
            return null;

        return !string.IsNullOrWhiteSpace(issueTime)
            && Iso8601Text.TryParseDateOrDateTime($"{issueDate!.Trim()}T{issueTime.Trim()}", out var utc)
                ? utc
                : date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}
