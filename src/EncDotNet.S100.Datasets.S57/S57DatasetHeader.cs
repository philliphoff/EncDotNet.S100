using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The identification facts of an S-57 dataset file (a base cell <c>….000</c>
/// or a sequential update <c>….001</c>, …), read from its leading
/// <c>DSID</c> / <c>DSPM</c> records without parsing the rest of the file.
/// </summary>
/// <remarks>
/// <para>
/// S-57 Part 3 §7.3 requires the Data Set General Information record
/// (<c>DSID</c>) to be the first data record and the Data Set Geographic
/// Reference record (<c>DSPM</c>) to follow it. <see cref="Read(Stream)"/>
/// therefore reads only the Data Descriptive Record plus the first two data
/// records (typically a few kilobytes) and hands them to the upstream
/// <c>EncDotNet.S57</c> parser. This is what lets a collection index report a
/// cell's edition, update and issue date without the cost of a full parse —
/// facts the exchange-set catalogue (<c>CATALOG.031</c>) does not carry.
/// </para>
/// </remarks>
public sealed record S57DatasetHeader
{
    /// <summary>The data set name (<c>DSNM</c>), e.g. <c>US5MA1BO.000</c>.</summary>
    public required string DataSetName { get; init; }

    /// <summary>The edition number (<c>EDTN</c>), or <see langword="null"/> when absent or non-numeric.</summary>
    public int? EditionNumber { get; init; }

    /// <summary>The update number (<c>UPDN</c>), or <see langword="null"/> when absent or non-numeric.</summary>
    public int? UpdateNumber { get; init; }

    /// <summary>The issue date (<c>ISDT</c>), or <see langword="null"/> when absent or malformed.</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>The update application date (<c>UADT</c>), or <see langword="null"/> when absent or malformed.</summary>
    public DateOnly? UpdateApplicationDate { get; init; }

    /// <summary>The intended usage (<c>INTU</c>, navigational purpose 1–6), or <see langword="null"/> when zero.</summary>
    public int? IntendedUsage { get; init; }

    /// <summary>The producing agency code (<c>AGEN</c>), or <see langword="null"/> when zero.</summary>
    public int? ProducingAgency { get; init; }

    /// <summary>The compilation scale (<c>DSPM</c> <c>CSCL</c>), or <see langword="null"/> when absent.</summary>
    public int? CompilationScale { get; init; }

    /// <summary>
    /// The number of ISO 8211 records read: the Data Descriptive Record plus
    /// the <c>DSID</c> and <c>DSPM</c> data records.
    /// </summary>
    private const int LeadingRecordCount = 3;

    /// <summary>The ISO 8211 leader length; its first five bytes encode the record length.</summary>
    private const int LeaderLength = 24;

    /// <summary>Upper bound on a single leading record, guarding against a corrupt length field.</summary>
    private const int MaxRecordLength = 1024 * 1024;

    /// <summary>
    /// Reads the header of the S-57 dataset file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The dataset file path.</param>
    /// <returns>The header, or <see langword="null"/> when the file carries no readable <c>DSID</c>.</returns>
    public static S57DatasetHeader? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
        return Read(stream);
    }

    /// <summary>
    /// Reads the header of an S-57 dataset from <paramref name="stream"/>,
    /// consuming only its leading records.
    /// </summary>
    /// <param name="stream">A stream positioned at the start of the dataset.</param>
    /// <returns>The header, or <see langword="null"/> when the stream carries no readable <c>DSID</c>.</returns>
    public static S57DatasetHeader? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = ReadLeadingRecords(stream);
        if (buffer is null)
            return null;

        S57Document document;
        try
        {
            document = S57DocumentReader.Read(buffer, logger: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any parse failure means "not a readable S-57 header" — for
            // example an S-101 cell, whose DSID lacks the S-57 subfields.
            return null;
        }

        if (document.DataSetIdentification is not { } dsid)
            return null;

        return new S57DatasetHeader
        {
            DataSetName = dsid.DataSetName,
            EditionNumber = ParseInt(dsid.EditionNumber),
            UpdateNumber = ParseInt(dsid.UpdateNumber),
            IssueDate = ParseDate(dsid.IssueDate),
            UpdateApplicationDate = ParseDate(dsid.UpdateApplicationDate),
            IntendedUsage = dsid.IntendedUsage > 0 ? dsid.IntendedUsage : null,
            ProducingAgency = dsid.ProducingAgency > 0 ? dsid.ProducingAgency : null,
            CompilationScale = document.DataSetParameters is { CompilationScale: > 0 } dspm
                ? dspm.CompilationScale
                : null,
        };
    }

    /// <summary>
    /// Copies the Data Descriptive Record and the first two data records into
    /// one buffer, using each record's leader-declared length. Returns
    /// <see langword="null"/> when the stream does not start with a
    /// well-formed ISO 8211 record.
    /// </summary>
    private static byte[]? ReadLeadingRecords(Stream stream)
    {
        using var output = new MemoryStream();
        var leader = new byte[LeaderLength];

        for (var i = 0; i < LeadingRecordCount; i++)
        {
            var read = stream.ReadAtLeast(leader, LeaderLength, throwOnEndOfStream: false);
            if (read < LeaderLength)
            {
                // A file with fewer records than expected (e.g. no DSPM) is
                // still worth parsing once the DDR and DSID are in hand.
                if (read == 0 && i >= 2)
                    break;
                return null;
            }

            if (!int.TryParse(leader.AsSpan(0, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length <= LeaderLength
                || length > MaxRecordLength)
            {
                return null;
            }

            output.Write(leader);
            var body = new byte[length - LeaderLength];
            if (stream.ReadAtLeast(body, body.Length, throwOnEndOfStream: false) < body.Length)
                return null;
            output.Write(body);
        }

        return output.ToArray();
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
